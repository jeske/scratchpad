﻿//#define OUTPUT
#define NODE4

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;

#if NODE4
using Node = SIMDPrototyping.Trees.Baseline.Node4;
#else
using Node = SIMDPrototyping.Trees.Baseline.Node2;
#endif


namespace SIMDPrototyping.Trees
{

    public class Tree<T> where T : IBounded
    {
        public const int ChildrenCapacity =
#if NODE4
            4;
#else
            2;
#endif

        struct Level
        {
            //Consider using a pointer to avoid pointless range checking.
            //Requires fixing. Use debug conditional stuff to ensure safety;
            //may want to use the array itself in debug mode too.
            //Doesn't change the other syntax really.
            public Node[] Nodes;
            public int Count;

            public int Add(ref Node node)
            {
                if (Count == Nodes.Length)
                {
                    var newNodes = new Node[Nodes.Length * 2];
                    Array.Copy(Nodes, newNodes, Nodes.Length);
                    Nodes = newNodes;
                }
                Nodes[Count] = node;
                return Count++;
            }
        }
        Level[] Levels;


        struct Leaf
        {
            public T Bounded;
            /// <summary>
            /// Which level the leaf is in.
            /// </summary>
            public int LevelIndex;
            /// <summary>
            /// Which node within the level the leaf is in.
            /// </summary>
            public int NodeIndex;
            /// <summary>
            /// Which child within the node the leaf is in.
            /// </summary>
            public int ChildIndex;
        }
        Leaf[] leaves;
        int leafCount;

        public int LeafCount
        {
            get
            {
                return leafCount;
            }
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        int AddLeaf(T leaf, int levelIndex, int nodeIndex, int childIndex)
        {
            if (leafCount == leaves.Length)
            {
                var newLeaves = new Leaf[leafCount * 2];
                Array.Copy(leaves, newLeaves, leafCount);
                leaves = newLeaves;
            }
            leaves[leafCount].Bounded = leaf;
            leaves[leafCount].LevelIndex = levelIndex;
            leaves[leafCount].NodeIndex = nodeIndex;
            leaves[LeafCount].ChildIndex = childIndex;
            return leafCount++;
        }

        private void EnsureLevel(int nextLevel)
        {
            if (nextLevel >= Levels.Length)
            {
                var newLevels = new Level[Levels.Length * 2];
                Array.Copy(Levels, newLevels, Levels.Length);
                for (int i = Levels.Length; i < newLevels.Length; ++i)
                {
                    //Assume the same size for subsequent levels. While they could grow exponentially,
                    //typically the tree will not be perfectly balanced.
                    newLevels[i] = new Level { Nodes = new Node[Levels[Levels.Length - 1].Nodes.Length] };
                }
                Levels = newLevels;
            }
            if (nextLevel > maximumDepth)
                maximumDepth = nextLevel;

        }




        int maximumDepth;
        /// <summary>
        /// Gets the index of the deepest tree layer that contains any nodes.
        /// </summary>
        public int MaximumDepth
        {
            get
            {
                return maximumDepth;
            }
        }

        public Tree(int initialLeafCapacity = 4096, int initialTreeDepth = 24)
        {
            if (initialTreeDepth <= 0)
                throw new ArgumentException("Initial tree depth must be positive.");
            if (initialLeafCapacity <= 0)
                throw new ArgumentException("Initial leaf capacity must be positive.");

            Levels = new Level[initialTreeDepth];
            var maximumNodeCount = (int)Math.Ceiling(initialTreeDepth / (double)Vector<float>.Count);
            for (int i = 0; i < Levels.Length; ++i)
            {
                Levels[i] = new Level { Nodes = new Node[Math.Min(initialLeafCapacity, (long)Math.Pow(2, Math.Min(25, i)))] };
            }
            InitializeNode(out Levels[0].Nodes[0]);
            Levels[0].Count = 1;

            leaves = new Leaf[initialLeafCapacity];

        }

        //Node initialNode;
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        void InitializeNode(out Node node)
        {
#if NODE4
            //could load a premade one instead.
            node.A = new BoundingBox { Min = new Vector3(float.MaxValue), Max = new Vector3(-float.MaxValue) };
            node.B = node.A;
            node.C = node.A;
            node.D = node.A;
            node.ChildA = -1;
            node.ChildB = -1;
            node.ChildC = -1;
            node.ChildD = -1;
            node.ChildCount = 0;
            //'no child' is encoded as -1. 
            //Leaf nodes are encoded as -(leafIndex + 2).
#else
#endif
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        int Encode(int index)
        {
            return -(index + 2);
        }

        string GetChoiceList(List<int> choices)
        {
            StringBuilder builder = new StringBuilder(choices.Count * 2);
            for (int i = 0; i < choices.Count; ++i)
            {
                builder.Append(choices[i]);
                if (i != choices.Count - 1)
                    builder.Append(", ");
            }
            return builder.ToString();
        }

        public unsafe void Insert(T leaf)
        {
            int levelIndex = 0;
            int nodeIndex = 0;
            BoundingBox box;
            leaf.GetBoundingBox(out box);
#if OUTPUT
            List<int> choices = new List<int>();
#endif
            while (true)
            {
                var level = Levels[levelIndex];
                //Which child should the leaf belong to?

                //Give the leaf to whichever node had the least volume change.
                var node = level.Nodes[nodeIndex];
                var boundingBoxes = &node.A;
                var children = &node.ChildA;
                int minimumIndex = 0;
                float minimumChange = float.MaxValue;
                BoundingBox merged = new BoundingBox();
                for (int i = 0; i < level.Nodes[nodeIndex].ChildCount; ++i)
                {
                    var oldVolume = Math.Max(0, BoundingBox.ComputeVolume(ref boundingBoxes[i]));
                    BoundingBox mergedCandidate;
                    BoundingBox.Merge(ref boundingBoxes[i], ref box, out mergedCandidate);
                    var newVolume = BoundingBox.ComputeVolume(ref mergedCandidate);
                    var change = newVolume - oldVolume;
                    if (newVolume < minimumChange)
                    {
                        minimumChange = newVolume;
                        minimumIndex = i;
                        merged = mergedCandidate;
                    }
                }
#if OUTPUT
                Console.WriteLine($"Minimum index: {minimumIndex}, minimum volume increase: {minimum}");
                choices.Add(minimumIndex);
#endif

                var childIndex = children[minimumIndex];

                if (childIndex < -1)
                {
                    //It's a leaf node.
                    //Create a new internal node with the new leaf and the old leaf as children.
                    var nextLevel = levelIndex + 1;
                    //this is the only place where a new level could potentially be created.
                    EnsureLevel(nextLevel);
                    Node newNode;
                    InitializeNode(out newNode);
                    newNode.ChildCount = 2;
                    //The first child of the new node is the old leaf. Insert its bounding box.
                    newNode.A = boundingBoxes[minimumIndex];
                    newNode.ChildA = children[minimumIndex];

                    //Insert the new leaf into the second child slot.
                    newNode.B = box;
                    var newNodeIndex = Levels[nextLevel].Add(ref newNode);
                    var leafIndex = AddLeaf(leaf, nextLevel, newNodeIndex, 1);
                    Levels[nextLevel].Nodes[newNodeIndex].ChildB = Encode(leafIndex);

                    //Update the old leaf node with the new index information.
                    var oldLeafIndex = Encode(childIndex);
                    leaves[oldLeafIndex].LevelIndex = nextLevel;
                    leaves[oldLeafIndex].NodeIndex = newNodeIndex;
                    leaves[oldLeafIndex].ChildIndex = 0;

                    //Update the original node's child pointer and bounding box.
                    children[minimumIndex] = newNodeIndex;
                    boundingBoxes[minimumIndex] = merged;

                    //BECAUSE THE POINTERS ARE TO A STACK VARIABLE, and not directly the heap data right now, you must copy the node back into the heap.
                    level.Nodes[nodeIndex] = node;

#if OUTPUT
                    Console.WriteLine($"Leaf {leafIndex} merged with existing leaf.");// New Node Children: {newNode.Children}, Old Node children: {level.Nodes[nodeIndex].Children}");
                    Console.WriteLine($"Choices: {GetChoiceList(choices)}");
#endif

                    break;
                }
                if (childIndex == -1)
                {
                    //There is no child at all.
                    //Put the new leaf here.
                    ++node.ChildCount;
                    var leafIndex = AddLeaf(leaf, levelIndex, nodeIndex, minimumIndex);
                    children[minimumIndex] = Encode(leafIndex);
                    boundingBoxes[minimumIndex] = merged;

                    //BECAUSE THE POINTERS ARE TO A STACK VARIABLE, and not directly the heap data right now, you must copy the node back into the heap.
                    level.Nodes[nodeIndex] = node;

#if OUTPUT
                    Console.WriteLine($"Leaf {leafIndex} inserted in empty slot.");
                    Console.WriteLine($"Choices: {GetChoiceList(choices)}");
#endif
                    break;
                }
                //It's an internal node. Traverse to the next node.
                boundingBoxes[minimumIndex] = merged;
                nodeIndex = children[minimumIndex];
                ++levelIndex;

                //BECAUSE THE POINTERS ARE TO A STACK VARIABLE, and not directly the heap data right now, you must copy the node back into the heap.
                level.Nodes[nodeIndex] = node;
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        void ComputeBoundingBox(ref BoundingBoxWide boundingBoxes, out BoundingBoxWide mergedWide)
        {
            //YIKES transposition
            BoundingBox merged;
            merged.Min = new Vector3(float.MaxValue);
            merged.Max = new Vector3(-float.MaxValue);
            for (int childIndex = 0; childIndex < Vector<int>.Count; ++childIndex)
            {
                var childMin = new Vector3(
                    boundingBoxes.Min.X[childIndex],
                    boundingBoxes.Min.Y[childIndex],
                    boundingBoxes.Min.Z[childIndex]);
                var childMax = new Vector3(
                    boundingBoxes.Max.X[childIndex],
                    boundingBoxes.Max.Y[childIndex],
                    boundingBoxes.Max.Z[childIndex]);
                merged.Min = Vector3.Min(merged.Min, childMin);
                merged.Max = Vector3.Max(merged.Max, childMax);
            }
            mergedWide = new BoundingBoxWide(ref merged);
        }



        public unsafe void Refit()
        {
            //Update the bounding boxes of every leaf-owner.
            //Note the scalar-ness of this. It seems like there should exist some way to vectorize it properly, though it may require changing things around.
            for (int i = 0; i < leafCount; ++i)
            {
                BoundingBox box;
                leaves[i].Bounded.GetBoundingBox(out (&Levels[leaves[i].LevelIndex].Nodes[leaves[i].NodeIndex].A)[leaves[i].ChildIndex]);
                //Console.WriteLine($"index reached: {i}, child index: {leaves[i].ChildIndex}, level: {leaves[i].LevelIndex}, node: { leaves[i].NodeIndex}");


            }
            //Go through each level, refitting as you go.
            //Note that the deepest level is skipped. It does not need to be tested; it's all leaves that were already updated.
            for (int levelIndex = maximumDepth - 1; levelIndex >= 0; --levelIndex)
            {
                //consider testing caching Levels[levelIndex]. It may have a minor effect.
                for (int nodeIndex = 0; nodeIndex < Levels[levelIndex].Count; ++nodeIndex)
                {
                    for (int childIndex = 0; childIndex < Levels[levelIndex].Nodes[nodeIndex].ChildCount; ++childIndex)
                    {

                        var childNodeIndex = (&Levels[levelIndex].Nodes[nodeIndex].ChildA)[childIndex];
                        if (childNodeIndex >= 0)
                        {
                            var childCount = Levels[levelIndex + 1].Nodes[childNodeIndex].ChildCount;
                            var boundingBoxSlot = (&Levels[levelIndex].Nodes[nodeIndex].A) + childIndex;
                            *boundingBoxSlot = Levels[levelIndex + 1].Nodes[childNodeIndex].A;
                            var childBoundingBoxes = &Levels[levelIndex + 1].Nodes[childNodeIndex].A;
                            for (int i = 1; i < childCount; ++i)
                            {
                                BoundingBox.Merge(ref *boundingBoxSlot, ref childBoundingBoxes[i], out *boundingBoxSlot);
                            }

                        }

                    }
                }
            }
        }


      

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        unsafe void Test<TResultList>(TraversalTarget* stack, ref int count, int stackCapacity, int level,
            ref BoundingBox query, Node* node,
            ref TResultList results) where TResultList : IList<T>
        {
            var boundingBoxes = &node->A;
            var children = &node->ChildA;
            for (int i = 0; i < node->ChildCount; ++i)
            {
                if (BoundingBox.Intersects(ref boundingBoxes[i], ref query))
                {
                    if (children[i] >= 0)
                    {
                        stack[count++] = new TraversalTarget { Level = level + 1, Node = children[i] };
                    }
                    else
                    {
                        results.Add(leaves[Encode(children[i])].Bounded);
                    }
                }

            }
        }

        //[MethodImpl(MethodImplOptions.AggressiveInlining)]
        public unsafe void Query<TResultList>(ref BoundingBox boundingBox, ref TResultList results) where TResultList : IList<T>
        {
            //TODO: could optimize this by keeping the next target out of the stack.
            var stackCapacity = (ChildrenCapacity - 1) * maximumDepth + 1;
            var stack = stackalloc TraversalTarget[stackCapacity];
            int count = 0;

            var boundingBoxWide = new BoundingBoxWide(ref boundingBox);
            Test(stack, ref count, stackCapacity, 0, ref boundingBox, Levels[0].Nodes[0], ref results);

            while (count > 0)
            {
                --count;
                var target = stack[count];

                Test(stack, ref count, stackCapacity, target.Level, ref boundingBox, Levels[target.Level].Nodes[target.Node], ref results);
            }
        }

        //[MethodImpl(MethodImplOptions.AggressiveInlining)]
        unsafe void TestRecursive<TResultList>(int level, int nodeIndex,
            ref BoundingBox query,
            ref TResultList results) where TResultList : IList<T>
        {
            Node* node = Levels[level].Nodes[nodeIndex];
            var boundingBoxes = &node->A;
            var children = &node->ChildA;
            for (int i = 0; i < node->ChildCount; ++i)
            {
                if (BoundingBox.Intersects(ref query, ref boundingBoxes[i]))
                {
                    if (children[i] >= 0)
                    {
                        TestRecursive(level + 1, children[i], ref query, ref results);
                    }
                    else if (children[i] < -1)
                    {
                        results.Add(leaves[Encode(children[i])].Bounded);
                    }
                }
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public unsafe void QueryRecursive<TResultList>(ref BoundingBox boundingBox, ref TResultList results) where TResultList : IList<T>
        {
            TestRecursive(0, 0, ref boundingBox, ref results);
        }

        unsafe void MeasureNodeOccupancy(int levelIndex, int nodeIndex, ref int nodeCount, ref int childCount)
        {
            ++nodeCount;

            Node* node = Levels[levelIndex].Nodes[nodeIndex];
            var children = &node->ChildA;
            childCount += node->ChildCount;
            for (int i = 0; i < node->ChildCount; ++i)
            {
                if (children[i] >= 0)
                {
                    MeasureNodeOccupancy(levelIndex + 1, children[i], ref nodeCount, ref childCount);
                }
            }
        }

        public void MeasureNodeOccupancy(out int nodeCount, out int childCount)
        {
            nodeCount = 0;
            childCount = 0;
            MeasureNodeOccupancy(0, 0, ref nodeCount, ref childCount);
        }

    }

    struct TraversalTarget
    {
        public int Level;
        public int Node;
    }
}
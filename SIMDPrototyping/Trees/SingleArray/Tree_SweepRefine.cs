﻿using BEPUutilities.DataStructures;
using BEPUutilities.ResourceManagement;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Threading.Tasks;

namespace SIMDPrototyping.Trees.SingleArray
{
    partial class Tree
    {
        struct SweepSubtree
        {
            public BoundingBox BoundingBox;
            public int Index;
            public int LeafCount;
        }

        unsafe void ComputeBoundingBox(SweepSubtree* subtrees, int subtreeStart, int subtreeCount, out BoundingBox boundingBox)
        {
            Debug.Assert(subtreeCount > 0);
            Debug.Assert(subtreeStart >= 0);
            subtrees += subtreeStart;

            boundingBox = subtrees->BoundingBox;
            for (int i = 1; i < subtreeCount; ++i)
            {
                BoundingBox.Merge(ref subtrees[i].BoundingBox, ref boundingBox, out boundingBox);
            }
        }

        unsafe void CentroidSort(SweepSubtree* subtrees, int subtreeStart, int subtreeCount, ref BoundingBox boundingBox)
        {
            //Compute dominant axis based on the bounding box.
            //Sort along that axis. This uses an in-place insertion sort for simplicity and to take advantage of the partially sorted data.
            var offset = boundingBox.Max - boundingBox.Min;
            int axisIndex;
            if (offset.X >= offset.Y && offset.X >= offset.Z)
            {
                //X is dominant.
                axisIndex = 0;
            }
            else if (offset.Y >= offset.Z)
            {
                //Y is dominant.
                axisIndex = 1;
            }
            else
            {
                //Z is dominant.
                axisIndex = 2;
            }
            //A variety of potential optimizations exist here.
            //Don't reallocate centroids (because JIT is forced to zero by roslyn), do swaps better, etc.
            //May also want to try not accessing components via pointer- may be faster to do directly, not much extra work.
            var centroids = stackalloc float[subtreeCount];
            subtrees += subtreeStart;
            for (int i = 0; i < subtreeCount; ++i)
            {
                centroids[i] = (&subtrees[i].BoundingBox.Min.X)[axisIndex] + (&subtrees[i].BoundingBox.Max.X)[axisIndex];
            }

            for (int i = 0; i < subtreeCount; ++i)
            {
                if (subtrees[i].LeafCount == 0)
                    Console.WriteLine("impossible");
            }
            for (int i = 1; i < subtreeCount; ++i)
            {
                var index = i;
                var previousIndex = index - 1;
                while (centroids[index] < centroids[previousIndex])
                {
                    var tempCentroid = centroids[index];
                    var tempSubtree = subtrees[index];
                    centroids[index] = centroids[previousIndex];
                    subtrees[index] = subtrees[previousIndex];
                    centroids[previousIndex] = tempCentroid;
                    subtrees[previousIndex] = tempSubtree;

                    if (subtrees[index].LeafCount == 0 || subtrees[previousIndex].LeafCount == 0)
                        Console.WriteLine("asD");

                    if (previousIndex == 0)
                        break;
                    index = previousIndex;
                    --previousIndex;
                }
            }

            for (int i = 0; i < subtreeCount; ++i)
            {
                if (subtrees[i].LeafCount == 0)
                    Console.WriteLine("impossible");
            }
        }

        unsafe int GetSplitIndex(SweepSubtree* subtrees, int subtreeStart, int subtreeCount, out BoundingBox a, out BoundingBox b, out int aLeafCount, out int bLeafCount)
        {
            //Precompute the bounding boxes to avoid redundant work.
            //TODO: Could avoid reallocating this array over and over again.
            //Build the bMerged lists by iterating backwards through the sorted leaves. Only do length - 1, since 0 < split < length - 1.
            //We want to just index bMerged directly with the candidate splitIndex to get the associated bounding boxes.
            //So, bMerged[0] should contain all bounding boxes from leaves[start + 1] to leaves[start + length - 1].
            //bMerged[bMerged.Length - 1] is just leaves[start + length - 1]'s bounding box.
            var bMergedLength = subtreeCount - 1;
            var bMerged = stackalloc BoundingBox[bMergedLength];
            var bLeafCounts = stackalloc int[bMergedLength];
            var lastIndex = subtreeStart + subtreeCount - 1;

            bMerged[bMergedLength - 1] = subtrees[lastIndex].BoundingBox;
            bLeafCounts[bMergedLength - 1] = subtrees[lastIndex].LeafCount;
            for (int i = bMergedLength - 2; i >= 0; --i)
            {
                var subtreeIndex = subtreeStart + 1 + i;
                bLeafCounts[i] = bLeafCounts[i + 1] + subtrees[subtreeIndex].LeafCount;
                BoundingBox.Merge(ref subtrees[subtreeIndex].BoundingBox, ref bMerged[i + 1], out bMerged[i]);

            }

            int lowestIndex = -1;
            float lowestCost = float.MaxValue;
            BoundingBox merged = new BoundingBox
            {
                Min = new Vector3(float.MaxValue),
                Max = new Vector3(-float.MaxValue)
            };
            a = b = merged;
            aLeafCount = bLeafCount = 0;
            int leafCount = 0;
            for (int i = 0; i < subtreeCount - 1; ++i)
            {
                leafCount += subtrees[subtreeStart + i].LeafCount;
                BoundingBox.Merge(ref merged, ref subtrees[subtreeStart + i].BoundingBox, out merged);
                var candidateCost = leafCount * ComputeBoundsMetric(ref merged) + bLeafCounts[i] * ComputeBoundsMetric(ref bMerged[i]);
                if (candidateCost < lowestCost)
                {
                    lowestCost = candidateCost;
                    lowestIndex = i;
                    a = merged;
                    b = bMerged[i];
                    aLeafCount = leafCount;
                    bLeafCount = bLeafCounts[i];
                }
            }
            
            return subtreeStart + lowestIndex + 1;
        }



        unsafe void SplitSubtreesIntoChildren(int depthRemaining, SweepSubtree* subtrees, int subtreeStart, int subtreeCount, ref BoundingBox boundingBox,
            Node* stagingNodes, int stagingNodeIndex, ref int stagingNodesCount, out float childrenTreeletsCost)
        {
            if (subtreeCount > 1)
            {
                CentroidSort(subtrees, subtreeStart, subtreeCount, ref boundingBox);

                BoundingBox a, b;
                int leafCountA, leafCountB;
                var splitIndex = GetSplitIndex(subtrees, subtreeStart, subtreeCount, out a, out b, out leafCountA, out leafCountB);
                if ((leafCountA + leafCountB) == 1)
                    Console.WriteLine("impossible");
                float costA, costB;
                if (depthRemaining > 0)
                {
                    --depthRemaining;
                    SplitSubtreesIntoChildren(depthRemaining, subtrees, subtreeStart, splitIndex - subtreeStart, ref a, stagingNodes, stagingNodeIndex, ref stagingNodesCount, out costA);
                    SplitSubtreesIntoChildren(depthRemaining, subtrees, splitIndex, subtreeStart + subtreeCount - splitIndex, ref b, stagingNodes, stagingNodeIndex, ref stagingNodesCount, out costB);
                }
                else
                {
                    //Recursion bottomed out. 
                    var stagingNode = stagingNodes + stagingNodeIndex;
                    var childIndexA = stagingNode->ChildCount++;
                    var childIndexB = stagingNode->ChildCount++;
                    Debug.Assert(stagingNode->ChildCount <= ChildrenCapacity);

                    var stagingBounds = &stagingNode->A;
                    var stagingChildren = &stagingNode->ChildA;
                    var stagingLeafCounts = &stagingNode->LeafCountA;

                    stagingBounds[childIndexA] = a;
                    stagingBounds[childIndexB] = b;

                    stagingLeafCounts[childIndexA] = leafCountA;
                    stagingLeafCounts[childIndexB] = leafCountB;

                    int subtreeCountA = splitIndex - subtreeStart;
                    int subtreeCountB = subtreeStart + subtreeCount - splitIndex;
                    if (subtreeCountA > 1)
                    {
                        stagingChildren[childIndexA] = CreateStagingNode(stagingNodeIndex, childIndexA, ref a, subtrees, subtreeStart, subtreeCountA,
                            stagingNodes, ref stagingNodesCount, out costA);
                        costA += ComputeBoundsMetric(ref a); //An internal node was created; measure its cost.
                    }
                    else
                    {
                        Debug.Assert(subtreeCountA == 1);
                        //Only one subtree. Don't create another node.
                        stagingChildren[childIndexA] = Encode(subtrees[subtreeStart].Index);
                        if (subtrees[subtreeStart].LeafCount != stagingLeafCounts[childIndexA])
                            Console.WriteLine("ASDF");
                        costA = 0;
                    }
                    if (subtreeCountB > 1)
                    {
                        stagingChildren[childIndexB] = CreateStagingNode(stagingNodeIndex, childIndexB, ref b, subtrees, splitIndex, subtreeCountB,
                            stagingNodes, ref stagingNodesCount, out costB);
                        costB += ComputeBoundsMetric(ref b); //An internal node was created; measure its cost.
                    }
                    else
                    {
                        Debug.Assert(subtreeCountB == 1);
                        //Only one subtree. Don't create another node.
                        stagingChildren[childIndexB] = Encode(subtrees[splitIndex].Index);
                        if (subtrees[splitIndex].LeafCount != stagingLeafCounts[childIndexB])
                            Console.WriteLine("ASDF");
                        costB = 0;
                    }
                }
                childrenTreeletsCost = costA + costB;
            }
            else
            {
                Debug.Assert(subtreeCount == 1);
                //Only one subtree. Just stick it directly into the node.
                var childIndex = stagingNodes[stagingNodeIndex].ChildCount++;
                Debug.Assert(stagingNodes[stagingNodeIndex].ChildCount <= ChildrenCapacity);
                (&stagingNodes[stagingNodeIndex].A)[childIndex] = subtrees[subtreeStart].BoundingBox;
                (&stagingNodes[stagingNodeIndex].ChildA)[childIndex] = Encode(subtrees[subtreeStart].Index);
                (&stagingNodes[stagingNodeIndex].LeafCountA)[childIndex] = subtrees[subtreeStart].LeafCount;
                //Subtrees cannot contribute to change in cost.
                childrenTreeletsCost = 0;
            }
        }

        unsafe int CreateStagingNode(int parentIndex, int indexInParent, ref BoundingBox boundingBox,
            SweepSubtree* subtrees, int subtreeStart, int subtreeCount,
            Node* stagingNodes, ref int stagingNodeCount, out float childTreeletsCost)
        {
            var stagingNodeIndex = stagingNodeCount++;
            var stagingNode = stagingNodes + stagingNodeIndex;

            if (subtreeCount < ChildrenCapacity)
            {
                //No need to do any sorting. This node can fit every remaining subtree.
                subtrees += subtreeStart;
                stagingNode->ChildCount = subtreeCount;
                var stagingNodeBounds = &stagingNode->A;
                var stagingNodeChildren = &stagingNode->ChildA;
                var leafCounts = &stagingNode->LeafCountA;
                for (int i = 0; i < subtreeCount; ++i)
                {
                    stagingNodeBounds[i] = subtrees[i].BoundingBox;
                    leafCounts[i] = subtrees[i].LeafCount;
                    stagingNodeChildren[i] = Encode(subtrees[i].Index);
                }
                //Because subtrees do not change in size, they cannot change the cost.
                childTreeletsCost = 0;
                return stagingNodeIndex;
            }

            const int recursionDepth = ChildrenCapacity == 32 ? 4 : ChildrenCapacity == 16 ? 3 : ChildrenCapacity == 8 ? 2 : ChildrenCapacity == 4 ? 1 : 0;


            SplitSubtreesIntoChildren(recursionDepth, subtrees, subtreeStart, subtreeCount, ref boundingBox, stagingNodes, stagingNodeIndex, ref stagingNodeCount, out childTreeletsCost);


            return stagingNodeIndex;

        }



        public unsafe void SweepRefine(int nodeIndex, ref QuickList<int> internalNodes, out bool nodesInvalidated)
        {
            const int maximumSubtrees = 1024;
            var subtrees = new QuickList<int>(BufferPools<int>.Thread, BufferPool<int>.GetPoolIndex(maximumSubtrees));
            int internalNodeStartIndex = internalNodes.Count;
            float originalTreeletCost;
            if (internalNodes.Count > nodeCount)

            {
                Console.WriteLine("asdf");
            }
            //Console.WriteLine($"previousCount: {internalNodes.Count}");
            CollectSubtrees(nodeIndex, maximumSubtrees, ref subtrees, ref internalNodes, out originalTreeletCost);
            if (internalNodes.Count > nodeCount)

            {
                Console.WriteLine("ASD");
            }

            //Gather necessary information from nodes. (TODO: This could be more efficiently gathered up front... collectsubtrees already touched most of this data!)
            var sweepSubtrees = stackalloc SweepSubtree[subtrees.Count];
            for (int i = 0; i < subtrees.Count; ++i)
            {
                var subtree = sweepSubtrees + i;
                subtree->Index = i;
                if (subtrees.Elements[i] >= 0)
                {
                    //It's an internal node.
                    var subtreeNode = nodes + subtrees.Elements[i];
                    var parentNode = nodes + subtreeNode->Parent;
                    subtree->BoundingBox = (&parentNode->A)[subtreeNode->IndexInParent];
                    subtree->LeafCount = (&parentNode->LeafCountA)[subtreeNode->IndexInParent];
                }
                else
                {
                    //It's a leaf node.
                    subtree->LeafCount = 1;
                    var leaf = leaves + Encode(subtrees.Elements[i]);
                    subtree->BoundingBox = (&nodes[leaf->NodeIndex].A)[leaf->ChildIndex];
                }
            }

            var node = nodes + nodeIndex;
            int parent = node->Parent;
            int indexInParent = node->IndexInParent;

            //Now perform a top-down sweep build.
            //TODO: this staging creation section is really the only part that is sweep-specific. The rest is common to any other kind of subtree-collection based refinement. 
            //If you end up making others, keep this in mind.
            int stagingNodeCount = 0;
            int stagingNodeCapacity = subtrees.Count - 1;
            var stagingNodes = stackalloc Node[stagingNodeCapacity];

            BoundingBox treeletBoundingBox;
            if (parent >= 0)
            {
                //This node is not the root, so we can look for the bounding box in the parent node.
                treeletBoundingBox = (&nodes[parent].A)[indexInParent];
            }
            else
            {
                //This node is the root, so the bounding box must be derived.
                treeletBoundingBox = node->A;
                for (int i = 1; i < node->ChildCount; ++i)
                {
                    BoundingBox.Merge(ref treeletBoundingBox, ref (&node->A)[i], out treeletBoundingBox);
                }
            }
            float newTreeletCost;
            CreateStagingNode(parent, indexInParent, ref treeletBoundingBox, sweepSubtrees, 0, subtrees.Count, stagingNodes, ref stagingNodeCount, out newTreeletCost);

            ValidateStaging(stagingNodes, sweepSubtrees, ref subtrees, parent, indexInParent);

            if (newTreeletCost < originalTreeletCost)
            {
                //Reify the nodes.
                ValidateLeaves();
                ReifyStagingNode(parent, indexInParent, stagingNodes, 0, stagingNodeCapacity, ref subtrees, ref internalNodes, out nodesInvalidated);
                ValidateLeaves();
            }
            else
            {
                //The internal nodes collected by the most recent iteration of CollectSubtrees weren't replaced! Get them out of the pool.
                //TODO: Would be nice to do this in a slightly less gross way.
                for (int i = internalNodes.Count - 1; i >= internalNodeStartIndex; --i)
                {
                    internalNodes.FastRemoveAt(i);
                }
                nodesInvalidated = false;
            }

            subtrees.Dispose();

        }

        

        unsafe void ValidateStaging(Node* stagingNodes, SweepSubtree* subtrees, ref QuickList<int> subtreeNodePointers, int treeletParent, int treeletIndexInParent)
        {
            return;
            int foundSubtrees, foundLeafCount;
            QuickList<int> collectedSubtreeReferences = new QuickList<int>(BufferPools<int>.Thread);
            ValidateStaging(stagingNodes, 0, subtrees, ref subtreeNodePointers, ref collectedSubtreeReferences, out foundSubtrees, out foundLeafCount);
            if (treeletParent < -1 || treeletParent >= nodeCount)
                throw new Exception("Bad treelet parent.");
            if (treeletIndexInParent < -1 || (treeletParent >= 0 && treeletIndexInParent >= nodes[treeletParent].ChildCount))
                throw new Exception("Bad treelet index in parent.");
            if (treeletParent >= 0 && (&nodes[treeletParent].LeafCountA)[treeletIndexInParent] != foundLeafCount)
            {
                throw new Exception("Bad leaf count.");
            }
            if (subtreeNodePointers.Count != foundSubtrees)
            {
                throw new Exception("Bad subtree found count.");
            }
            for (int i = 0; i < collectedSubtreeReferences.Count; ++i)
            {
                if (!subtreeNodePointers.Contains(collectedSubtreeReferences[i]) || !collectedSubtreeReferences.Contains(subtreeNodePointers[i]))
                    throw new Exception("Bad subtree reference.");
            }
            collectedSubtreeReferences.Dispose();
        }
        unsafe void ValidateStaging(Node* stagingNodes, int stagingNodeIndex, SweepSubtree* subtrees, ref QuickList<int> subtreeNodePointers, ref QuickList<int> collectedSubtreeReferences, out int foundSubtrees, out int foundLeafCount)
        {
            var stagingNode = stagingNodes + stagingNodeIndex;
            var children = &stagingNode->ChildA;
            var leafCounts = &stagingNode->LeafCountA;
            foundSubtrees = foundLeafCount = 0;
            for (int i = 0; i < stagingNode->ChildCount; ++i)
            {
                if (children[i] >= 0)
                {
                    int childFoundSubtrees, childFoundLeafCount;
                    ValidateStaging(stagingNodes, children[i], subtrees, ref subtreeNodePointers, ref collectedSubtreeReferences, out childFoundSubtrees, out childFoundLeafCount);
                    if (childFoundLeafCount != leafCounts[i])
                        throw new Exception("Bad leaf count.");
                    foundSubtrees += childFoundSubtrees;
                    foundLeafCount += childFoundLeafCount;
                }
                else
                {
                    var subtreeNodePointerIndex = Encode(children[i]);
                    var subtreeNodePointer = subtreeNodePointers.Elements[subtreeNodePointerIndex];
                    //Rather than looking up the shuffled SweepSubtree for information, just go back to the source.
                    if (subtreeNodePointer >= 0)
                    {
                        var node = nodes + subtreeNodePointer;
                        var totalLeafCount = 0;
                        for (int childIndex = 0; childIndex < node->ChildCount; ++childIndex)
                        {
                            totalLeafCount += (&node->LeafCountA)[childIndex];
                        }

                        if (leafCounts[i] != totalLeafCount)
                            throw new Exception("bad leaf count.");
                        foundLeafCount += totalLeafCount;
                    }
                    else
                    {
                        var leafIndex = Encode(subtreeNodePointer);
                        if (leafCounts[i] != 1)
                            throw new Exception("bad leaf count.");
                        foundLeafCount += 1;
                    }
                    ++foundSubtrees;
                    collectedSubtreeReferences.Add(subtreeNodePointerIndex);
                }
            }

        }


        private unsafe void TopDownSweepRefine(int nodeIndex, ref QuickList<int> spareNodes)
        {
            bool nodesInvalidated;
            Validate();
            SweepRefine(nodeIndex, ref spareNodes, out nodesInvalidated);
            Validate();
            //The root of the tree is guaranteed to stay in position, so nodeIndex is still valid.

            //Node pointers can be invalidated, so don't hold a reference between executions.
            for (int i = 0; i < nodes[nodeIndex].ChildCount; ++i)
            {
                var child = (&nodes[nodeIndex].ChildA)[i];
                if (child >= 0)
                {
                    TopDownSweepRefine(child, ref spareNodes);
                }
            }
        }
        public unsafe void TopDownSweepRefine()
        {
            var spareNodes = new QuickList<int>(BufferPools<int>.Thread, 8);
            TopDownSweepRefine(0, ref spareNodes);
            RemoveUnusedInternalNodes(ref spareNodes);
            spareNodes.Dispose();
        }
    }
}

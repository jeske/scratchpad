﻿using System;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;

namespace SIMDPrototyping.Trees
{
    //[StructLayout(LayoutKind.Explicit, Size = 32)]
    public struct BoundingBox
    {
        //[FieldOffset(0)]
        public Vector3 Min;
        //[FieldOffset(16)]
        public Vector3 Max;


        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool Intersects(ref BoundingBox a, ref BoundingBox b)
        {
            ////May be able to do better than this. Consider unbranching it.
            //return Vector3.Clamp(a.Min, b.Min, b.Max) == a.Min || Vector3.Clamp(a.Max, b.Min, b.Max) == a.Max;
            //return !(a.Max.X < b.Min.X || a.Max.Y < b.Min.Y || a.Max.Z < b.Min.Z ||
            //         b.Max.X < a.Min.X || b.Max.Y < a.Min.Y || b.Max.Z < a.Min.Z);
            //return !(a.Max.X < b.Min.X | a.Max.Y < b.Min.Y | a.Max.Z < b.Min.Z |
            //         b.Max.X < a.Min.X | b.Max.Y < a.Min.Y | b.Max.Z < a.Min.Z);
            return a.Max.X >= b.Min.X & a.Max.Y >= b.Min.Y & a.Max.Z >= b.Min.Z &
                     b.Max.X >= a.Min.X & b.Max.Y >= a.Min.Y & b.Max.Z >= a.Min.Z;
            //return a.Max.X >= b.Min.X && a.Max.Y >= b.Min.Y && a.Max.Z >= b.Min.Z &&
            //         b.Max.X >= a.Min.X && b.Max.Y >= a.Min.Y && b.Max.Z >= a.Min.Z;
            //return Vector3.Min(Vector3.Max(a.Min, b.Min), b.Max) == a.Min ||
            //    Vector3.Min(Vector3.Max(a.Max, b.Min), b.Max) == a.Max || 
            //    Vector3.Min(Vector3.Max(b.Min, a.Min), a.Max) == b.Min ||
            //    Vector3.Min(Vector3.Max(b.Max, a.Min), a.Max) == b.Max;

            //This could be changed to result = And(GEQ(a.Max, b.Min), GEQ(b.Max, a.Min))
            //Then, horizontal And...
            //Could implement as dot(result, One). If that's -3, then we're good!
            //geq, geq, and, dot, load 3, compare. Six instructions instead of eleven, and no scalar swap.

        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static unsafe float ComputeVolume(ref BoundingBox a)
        {
            var diagonal = (a.Max - a.Min);
            return diagonal.X * diagonal.Y * diagonal.Z;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void Merge(ref BoundingBox a, ref BoundingBox b, out BoundingBox merged)
        {
            merged.Min = Vector3.Min(a.Min, b.Min);
            merged.Max = Vector3.Max(a.Max, b.Max);
        }

        public override string ToString()
        {
            return $"({Min.ToString()}, {Max.ToString()})";
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct BoundingBoxWide
    {
        public Vector3Wide Min;
        public Vector3Wide Max;


        public BoundingBoxWide(ref BoundingBox boundingBox)
        {
            Min = new Vector3Wide(ref boundingBox.Min);
            Max = new Vector3Wide(ref boundingBox.Max);
        }

        public unsafe BoundingBoxWide(BoundingBox* boundingBoxes, Vector<int>[] masks)
        {
            Min = new Vector3Wide(ref boundingBoxes[0].Min);
            Max = new Vector3Wide(ref boundingBoxes[0].Max);
            for (int i = 1; i < Vector<float>.Count; ++i)
            {
                BoundingBoxWide wide = new BoundingBoxWide(ref boundingBoxes[i]);
                ConditionalSelect(ref masks[i], ref wide, ref this, out this);
            }
        }



        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public unsafe static void Intersects(ref BoundingBoxWide a, ref BoundingBoxWide b, out Vector<int> intersectionMask)
        {
            //If any minimum exceeds the other maximum, there can be no collision.
            //On the flipside, if the all minimums are less than the opposing maximums, then they must be colliding.
            var c1X = Vector.LessThanOrEqual(a.Min.X, b.Max.X);
            var c1Y = Vector.LessThanOrEqual(a.Min.Y, b.Max.Y);
            var c1Z = Vector.LessThanOrEqual(a.Min.Z, b.Max.Z);
            var c2X = Vector.LessThanOrEqual(b.Min.X, a.Max.X);
            var c2Y = Vector.LessThanOrEqual(b.Min.Y, a.Max.Y);
            var c2Z = Vector.LessThanOrEqual(b.Min.Z, a.Max.Z);
            var or1 = Vector.BitwiseAnd(c1X, c1Y);
            var or2 = Vector.BitwiseAnd(c1Z, c2X);
            var or3 = Vector.BitwiseAnd(c2Y, c2Z);
            var or4 = Vector.BitwiseAnd(or1, or2);
            intersectionMask = Vector.BitwiseAnd(or3, or4);


        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public unsafe static void Intersects2(ref BoundingBoxWide a, ref BoundingBoxWide b, out Vector<int> intersectionMask)
        {

            var minX = Vector.Max(a.Min.X, b.Min.X);
            var minY = Vector.Max(a.Min.Y, b.Min.Y);
            var minZ = Vector.Max(a.Min.Z, b.Min.Z);
            var maxX = Vector.Min(a.Max.X, b.Max.X);
            var maxY = Vector.Min(a.Max.Y, b.Max.Y);
            var maxZ = Vector.Min(a.Max.Z, b.Max.Z);
            var xLeq = Vector.LessThanOrEqual(minX, maxX);
            var yLeq = Vector.LessThanOrEqual(minY, maxY);
            var zLeq = Vector.LessThanOrEqual(minZ, maxZ);
            intersectionMask = Vector.BitwiseAnd(xLeq, Vector.BitwiseAnd(yLeq, zLeq));


        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void ConditionalSelect(ref Vector<int> mask, ref BoundingBoxWide a, ref BoundingBoxWide b, out BoundingBoxWide result)
        {
            Vector3Wide.ConditionalSelect(ref mask, ref a.Min, ref b.Min, out result.Min);
            Vector3Wide.ConditionalSelect(ref mask, ref a.Max, ref b.Max, out result.Max);
        }

        public static void Merge(ref BoundingBoxWide a, ref BoundingBoxWide b, out BoundingBoxWide merged)
        {
            Vector3Wide.Min(ref a.Min, ref b.Min, out merged.Min);
            Vector3Wide.Max(ref a.Max, ref b.Max, out merged.Max);
        }

        public static void ComputeVolume(ref BoundingBoxWide boxes, out Vector<float> volumes)
        {
            volumes = new Vector<float>();

            Vector3Wide span;
            Vector3Wide.Subtract(ref boxes.Max, ref boxes.Min, out span);
            volumes = span.X * span.Y * span.Z;
        }

        public override string ToString()
        {
            StringBuilder stringBuilder = new StringBuilder();
            for (int i = 0; i < Vector<float>.Count; ++i)
            {
                BoundingBox box;
                box.Min = new Vector3(Min.X[i], Min.Y[i], Min.Z[i]);
                box.Max = new Vector3(Max.X[i], Max.Y[i], Max.Z[i]);
                stringBuilder.Append(box.ToString());
                if (i != Vector<float>.Count - 1)
                    stringBuilder.Append(", ");
            }
            return stringBuilder.ToString();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void GetBoundingBox(ref BoundingBoxWide boundingBoxes, int i, out BoundingBoxWide wide)
        {
            wide.Min.X = new Vector<float>(boundingBoxes.Min.X[i]);
            wide.Min.Y = new Vector<float>(boundingBoxes.Min.Y[i]);
            wide.Min.Z = new Vector<float>(boundingBoxes.Min.Z[i]);
            wide.Max.X = new Vector<float>(boundingBoxes.Max.X[i]);
            wide.Max.Y = new Vector<float>(boundingBoxes.Max.Y[i]);
            wide.Max.Z = new Vector<float>(boundingBoxes.Max.Z[i]);
            //var boundingBox = new BoundingBox
            //{
            //    Min = new Vector3(boundingBoxes.Min.X[i], boundingBoxes.Min.Y[i], boundingBoxes.Min.Z[i]),
            //    Max = new Vector3(boundingBoxes.Max.X[i], boundingBoxes.Max.X[i], boundingBoxes.Max.X[i])
            //};
            //wide = new BoundingBoxWide(ref boundingBox);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void GetBoundingBox(ref BoundingBoxWide boundingBoxes, int i, out BoundingBox box)
        {
            box = new BoundingBox
            {
                Min = new Vector3(boundingBoxes.Min.X[i], boundingBoxes.Min.Y[i], boundingBoxes.Min.Z[i]),
                Max = new Vector3(boundingBoxes.Max.X[i], boundingBoxes.Max.Y[i], boundingBoxes.Max.Z[i])
            };
        }
    }
}

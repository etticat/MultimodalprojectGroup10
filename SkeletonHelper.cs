﻿namespace ShapeGame.Utils
{
    using System;
    using Microsoft.Kinect;


    
    public struct Bone
    {
        public JointType Joint1;
        public JointType Joint2;

        public Bone(JointType j1, JointType j2)
        {
            this.Joint1 = j1;
            this.Joint2 = j2;
        }
    }

    public struct Segment
    {
        public double X1;
        public double Y1;
        public double X2;
        public double Y2;
        public double Radius;

        public Segment(double x, double y)
        {
            this.Radius = 1;
            this.X1 = this.X2 = x;
            this.Y1 = this.Y2 = y;
        }

        public Segment(double x1, double y1, double x2, double y2)
        {
            this.Radius = 1;
            this.X1 = x1;
            this.Y1 = y1;
            this.X2 = x2;
            this.Y2 = y2;
        }

        public bool IsCircle()
        {
            return (this.X1 == this.X2) && (this.Y1 == this.Y2);
        }
    }

    public struct BoneData
    {
        public Segment Segment;
        public Segment LastSegment;
        public DateTime TimeLastUpdated;

        private const double Smoothing = 0.8;

        public BoneData(Segment s)
        {
            this.Segment = this.LastSegment = s;
            this.TimeLastUpdated = DateTime.Now;
        }
        public void UpdateSegment(Segment s)
        {
            this.LastSegment = this.Segment;
            this.Segment = s;
            
            DateTime cur = DateTime.Now;
            double fMs = cur.Subtract(this.TimeLastUpdated).TotalMilliseconds;
            if (fMs < 10.0)
            {
                fMs = 10.0;
            }

            double fps = 1000.0 / fMs;
            this.TimeLastUpdated = cur;
            
        }
        
        public Segment GetEstimatedSegment(DateTime cur)
        {
            Segment estimate = this.Segment;
            double fMs = cur.Subtract(this.TimeLastUpdated).TotalMilliseconds;
            if (this.Segment.IsCircle())
            {
                estimate.X2 = estimate.X1;
                estimate.Y2 = estimate.Y1;
            }

            return estimate;
        }
    }
}
//------------------------------------------------------------------------------
// <copyright file="FallingThings.cs" company="Microsoft">
//     Copyright (c) Microsoft Corporation.  All rights reserved.
// </copyright>
//------------------------------------------------------------------------------

// This module contains code to do display falling shapes, and do
// hit testing against a set of segments provided by the Kinect NUI, and
// have shapes react accordingly.

namespace ShapeGame
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.Windows;
    using System.Windows.Controls;
    using System.Windows.Media;
    using System.Windows.Shapes;
    using Microsoft.Kinect;
    using ShapeGame.Utils;

    // FallingThings is the main class to draw and maintain positions of falling shapes.  It also does hit testing
    // and appropriate bouncing.
    public class FallingShapes
    {
        private const double BaseGravity = 0.017;
        private const double BaseAirFriction = 0.994;

        private readonly Dictionary<PolyType, PolyDef> polyDefs = new Dictionary<PolyType, PolyDef>
            {
                { PolyType.Triangle, new PolyDef { Sides = 3, Skip = 1 } },
                { PolyType.Star, new PolyDef { Sides = 5, Skip = 2 } },
                { PolyType.Pentagon, new PolyDef { Sides = 5, Skip = 1 } },
                { PolyType.Square, new PolyDef { Sides = 4, Skip = 1 } },
                { PolyType.Hex, new PolyDef { Sides = 6, Skip = 1 } },
                { PolyType.Star7, new PolyDef { Sides = 7, Skip = 3 } },
                { PolyType.Circle, new PolyDef { Sides = 1, Skip = 1 } },
                { PolyType.Bubble, new PolyDef { Sides = 0, Skip = 1 } }
            };

        private readonly List<Thing> things = new List<Thing>();
        private readonly Random rnd = new Random();
        private readonly int maxThings;
        private readonly int intraFrames = 1;
        private readonly Dictionary<int, int> scores = new Dictionary<int, int>();
        private const double DissolveTime = 0.4;
        private Rect sceneRect;
        private double targetFrameRate = 60;
        private double dropRate = 2.0;
        private double shapeSize = 1.0;
        private double baseShapeSize = 20;
        private double gravity = BaseGravity;
        private double gravityFactor = 1.0;
        private double airFriction = BaseAirFriction;
        private int frameCount;
        private bool doRandomColors = true;
        private double expandingRate = 1.0;
        private System.Windows.Media.Color baseColor = System.Windows.Media.Color.FromRgb(0, 0, 0);
        private PolyType polyTypes = PolyType.All;
        private DateTime gameStartTime;

        public FallingShapes(int maxThings, double framerate, int intraFrames)
        {
            this.maxThings = maxThings;
            this.intraFrames = intraFrames;
            this.targetFrameRate = framerate * intraFrames;
            this.SetGravity(this.gravityFactor);
            this.sceneRect.X = this.sceneRect.Y = 0;
            this.sceneRect.Width = this.sceneRect.Height = 100;
            this.shapeSize = this.sceneRect.Height * this.baseShapeSize / 1000.0;
            this.expandingRate = Math.Exp(Math.Log(6.0) / (this.targetFrameRate * DissolveTime));
        }

        public enum ThingState
        {
            Falling = 0,
            Bouncing = 1,
            Dissolving = 2,
            Remove = 3
        }

        public static Label MakeSimpleLabel(string text, Rect bounds, System.Windows.Media.Brush brush)
        {
            Label label = new Label { Content = text };
            if (bounds.Width != 0)
            {
                label.SetValue(Canvas.LeftProperty, bounds.Left);
                label.SetValue(Canvas.TopProperty, bounds.Top);
                label.Width = bounds.Width;
                label.Height = bounds.Height;
            }

            label.Foreground = brush;
            label.FontFamily = new System.Windows.Media.FontFamily("Arial");
            label.FontWeight = FontWeight.FromOpenTypeWeight(600);
            label.FontStyle = FontStyles.Normal;
            label.HorizontalAlignment = HorizontalAlignment.Center;
            label.VerticalAlignment = VerticalAlignment.Center;
            return label;
        }

        public void SetFramerate(double actualFramerate)
        {
            this.targetFrameRate = actualFramerate * this.intraFrames;
            this.expandingRate = Math.Exp(Math.Log(6.0) / (this.targetFrameRate * DissolveTime));
            if (this.gravityFactor != 0)
            {
                this.SetGravity(this.gravityFactor);
            }
        }

        public void SetBoundaries(Rect r)
        {
            this.sceneRect = r;
            this.shapeSize = r.Height * this.baseShapeSize / 1000.0;
        }

        public void SetDropRate(double f)
        {
            this.dropRate = f;
        }

        public void SetSize(double f)
        {
            this.baseShapeSize = f;
            this.shapeSize = this.sceneRect.Height * this.baseShapeSize / 1000.0;
        }

        public void SetShapesColor(System.Windows.Media.Color color, bool doRandom)
        {
            this.doRandomColors = doRandom;
            this.baseColor = color;
        }

        public void Reset()
        {
            for (int i = 0; i < this.things.Count; i++)
            {
                Thing thing = this.things[i];
                if ((thing.State == ThingState.Bouncing) || (thing.State == ThingState.Falling))
                {
                    thing.State = ThingState.Dissolving;
                    thing.Dissolve = 0;
                    this.things[i] = thing;
                }
            }

            this.gameStartTime = DateTime.Now;
            this.scores.Clear();
        }

        public void StartGame()
        {
            this.gameStartTime = DateTime.Now;
            this.scores.Clear();
        }

        public void SetGravity(double f)
        {
            this.gravityFactor = f;
            this.gravity = f * BaseGravity / this.targetFrameRate / Math.Sqrt(this.targetFrameRate) / Math.Sqrt(this.intraFrames);
            this.airFriction = f == 0 ? 0.997 : Math.Exp(Math.Log(1.0 - ((1.0 - BaseAirFriction) / f)) / this.intraFrames);

            if (f == 0)
            {
                // Stop all movement as well!
                for (int i = 0; i < this.things.Count; i++)
                {
                    Thing thing = this.things[i];
                    thing.XVelocity = thing.YVelocity = 0;
                    this.things[i] = thing;
                }
            }
        }

        public void SetPolies(PolyType polies)
        {
            this.polyTypes = polies;
        }
        

        public void AdvanceFrame()
        {
            // Move all things by one step, accounting for gravity
            for (int thingIndex = 0; thingIndex < this.things.Count; thingIndex++)
            {
                Thing thing = this.things[thingIndex];
                thing.Center.Offset(thing.XVelocity, thing.YVelocity);
                thing.YVelocity += this.gravity * this.sceneRect.Height;
                thing.YVelocity *= this.airFriction;
                thing.XVelocity *= this.airFriction;
                thing.Theta += thing.SpinRate;

                // bounce off walls
                if ((thing.Center.X - thing.Size < 0) || (thing.Center.X + thing.Size > this.sceneRect.Width))
                {
                    thing.XVelocity = -thing.XVelocity;
                    thing.Center.X += thing.XVelocity;
                }

                // Then get rid of one if any that fall off the bottom
                if (thing.Center.Y - thing.Size > this.sceneRect.Bottom)
                {
                    thing.State = ThingState.Remove;
                }

                // Get rid of after dissolving.
                if (thing.State == ThingState.Dissolving)
                {
                    thing.Dissolve += 1 / (this.targetFrameRate * DissolveTime);
                    thing.Size *= this.expandingRate;
                    if (thing.Dissolve >= 1.0)
                    {
                        thing.State = ThingState.Remove;
                    }
                }

                this.things[thingIndex] = thing;
            }

            // Then remove any that should go away now
            for (int i = 0; i < this.things.Count; i++)
            {
                Thing thing = this.things[i];
                if (thing.State == ThingState.Remove)
                {
                    this.things.Remove(thing);
                    i--;
                }
            }
            
        }

        public void DrawFrame(UIElementCollection children)
        {
            this.frameCount++;

            // Draw all shapes in the scene
            for (int i = 0; i < this.things.Count; i++)
            {
                Thing thing = this.things[i];
                if (thing.Brush == null)
                {
                    thing.Brush = new SolidColorBrush(thing.Color);
                    double factor = 0.4 + (((double)thing.Color.R + thing.Color.G + thing.Color.B) / 1600);
                    thing.Brush2 =
                        new SolidColorBrush(
                            System.Windows.Media.Color.FromRgb(
                                (byte)(255 - ((255 - thing.Color.R) * factor)),
                                (byte)(255 - ((255 - thing.Color.G) * factor)),
                                (byte)(255 - ((255 - thing.Color.B) * factor))));
                    thing.BrushPulse = new SolidColorBrush(System.Windows.Media.Color.FromRgb(255, 255, 255));
                }

                if (thing.State == ThingState.Bouncing)
                {
                    // Pulsate edges
                    double alpha = Math.Cos((0.15 * (thing.FlashCount++) * thing.Hotness) * 0.5) + 0.5;

                    children.Add(
                        this.MakeSimpleShape(
                            this.polyDefs[thing.Shape].Sides,
                            this.polyDefs[thing.Shape].Skip,
                            thing.Size,
                            thing.Theta,
                            thing.Center,
                            thing.Brush,
                            thing.BrushPulse,
                            thing.Size * 0.1,
                            alpha));
                    this.things[i] = thing;
                }
                else
                {
                    if (thing.State == ThingState.Dissolving)
                    {
                        thing.Brush.Opacity = 1.0 - (thing.Dissolve * thing.Dissolve);
                    }

                    children.Add(
                        this.MakeSimpleShape(
                            this.polyDefs[thing.Shape].Sides,
                            this.polyDefs[thing.Shape].Skip,
                            thing.Size,
                            thing.Theta,
                            thing.Center,
                            thing.Brush,
                            (thing.State == ThingState.Dissolving) ? null : thing.Brush2,
                            1,
                            1));
                }
            }

            // Show scores
            if (this.scores.Count != 0)
            {
                int i = 0;
                foreach (var score in this.scores)
                {
                    Label label = MakeSimpleLabel(
                        score.Value.ToString(CultureInfo.InvariantCulture),
                        new Rect(
                            (0.02 + (i * 0.6)) * this.sceneRect.Width,
                            0.01 * this.sceneRect.Height,
                            0.4 * this.sceneRect.Width,
                            0.3 * this.sceneRect.Height), 
                            new SolidColorBrush(System.Windows.Media.Color.FromArgb(200, 255, 255, 255)));
                    label.FontSize = Math.Max(1, Math.Min(this.sceneRect.Width / 12, this.sceneRect.Height / 12));
                    children.Add(label);
                    i++;
                }
            }
            
        }

        private static double SquaredDistance(double x1, double y1, double x2, double y2)
        {
            return ((x2 - x1) * (x2 - x1)) + ((y2 - y1) * (y2 - y1));
        }

        private void AddToScore(int player, int points, System.Windows.Point center)
        {
            if (this.scores.ContainsKey(player))
            {
                this.scores[player] = this.scores[player] + points;
            }
            else
            {
                this.scores.Add(player, points);
            }

            FlyingText.NewFlyingText(this.sceneRect.Width / 300, center, "+" + points);
        }
        
        private Shape MakeSimpleShape(
            int numSides,
            int skip,
            double size,
            double spin,
            System.Windows.Point center,
            System.Windows.Media.Brush brush,
            System.Windows.Media.Brush brushStroke,
            double strokeThickness,
            double opacity)
        {
            if (numSides <= 1)
            {
                var circle = new Ellipse { Width = size * 2, Height = size * 2, Stroke = brushStroke };
                if (circle.Stroke != null)
                {
                    circle.Stroke.Opacity = opacity;
                }

                circle.StrokeThickness = strokeThickness * ((numSides == 1) ? 1 : 2);
                circle.Fill = (numSides == 1) ? brush : null;
                circle.SetValue(Canvas.LeftProperty, center.X - size);
                circle.SetValue(Canvas.TopProperty, center.Y - size);
                return circle;
            }

            var points = new PointCollection(numSides + 2);
            double theta = spin;
            for (int i = 0; i <= numSides + 1; ++i)
            {
                points.Add(new System.Windows.Point((Math.Cos(theta) * size) + center.X, (Math.Sin(theta) * size) + center.Y));
                theta = theta + (2.0 * Math.PI * skip / numSides);
            }

            var polyline = new Polyline { Points = points, Stroke = brushStroke };
            if (polyline.Stroke != null)
            {
                polyline.Stroke.Opacity = opacity;
            }

            polyline.Fill = brush;
            polyline.FillRule = FillRule.Nonzero;
            polyline.StrokeThickness = strokeThickness;
            return polyline;
        }

        internal struct PolyDef
        {
            public int Sides;
            public int Skip;
        }

        // The Thing struct represents a single object that is flying through the air, and
        // all of its properties.
        private struct Thing
        {
            public System.Windows.Point Center;
            public double Size;
            public double Theta;
            public double SpinRate;
            public double YVelocity;
            public double XVelocity;
            public PolyType Shape;
            public System.Windows.Media.Color Color;
            public System.Windows.Media.Brush Brush;
            public System.Windows.Media.Brush Brush2;
            public System.Windows.Media.Brush BrushPulse;
            public double Dissolve;
            public ThingState State;
            public DateTime TimeLastHit;
            public double AvgTimeBetweenHits;
            public int TouchedBy;               // Last player to touch this thing
            public int Hotness;                 // Score level
            public int FlashCount;
            
           
        }
    }
}

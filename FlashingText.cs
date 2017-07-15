
namespace ShapeGame
{
    using System;
    using System.Collections.Generic;
    using System.Windows;
    using System.Windows.Controls;
    using System.Windows.Media;
    
    public class FlashingText
    {
        private static readonly List<FlashingText> FlyingTexts = new List<FlashingText>();
        private readonly double fontGrow;
        private readonly string text;
        private System.Windows.Point center;
        private System.Windows.Media.Brush brush;
        private double fontSize;
        private double alpha;
        private Label label;

        public FlashingText(string s, double size, System.Windows.Point center)
        {
            this.text = s;
            this.fontSize = Math.Max(1, size);
            this.fontGrow = Math.Sqrt(size) * 0.1;
            this.center = center;
            this.alpha = 1.0;
            this.label = null;
            this.brush = null;
        }

        public static void NewFlyingText(double size, System.Windows.Point center, string s)
        {
            FlyingTexts.Add(new FlashingText(s, size, center));
        }

        public static void Draw(UIElementCollection children)
        {
            for (int i = 0; i < FlyingTexts.Count; i++)
            {
                FlashingText flyout = FlyingTexts[i];
                if (flyout.alpha <= 0)
                {
                    FlyingTexts.Remove(flyout);
                    i--;
                }
            }

            foreach (var flyout in FlyingTexts)
            {
                flyout.Advance();
                children.Add(flyout.label);
            }
        }

        private void Advance()
        {
            this.alpha -= 0.005;
            if (this.alpha < 0)
            {
                this.alpha = 0;
            }

            if (this.brush == null)
            {
                this.brush = new SolidColorBrush(System.Windows.Media.Color.FromArgb(255, 255, 255, 255));
            }


            if (this.label == null)
            {
                this.label = MakeSimpleLabel(this.text, new Rect(0, 0, 0, 0), this.brush);
            }
            this.brush.Opacity = Math.Pow(this.alpha, 1.5);
            this.label.Foreground = this.brush;
            this.fontSize += this.fontGrow;
            this.label.FontSize = Math.Max(1, this.fontSize);
            Rect renderRect = new Rect(this.label.RenderSize);
            this.label.SetValue(Canvas.LeftProperty, this.center.X - (renderRect.Width / 2));
            this.label.SetValue(Canvas.TopProperty, this.center.Y - (renderRect.Height / 2));
        }

        public Label MakeSimpleLabel(string text, Rect bounds, System.Windows.Media.Brush brush)
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
    }
}

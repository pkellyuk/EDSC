using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using EDSC.Models;
using System;
using System.Diagnostics;

namespace EDSC.Controls
{
    /// <summary>
    /// Draws a <see cref="FaceMeshFrame"/> as line segments on a black background using the
    /// Avalonia drawing context directly. There is no bitmap, no JPEG and no image decode in
    /// this path: a frame becomes a handful of geometry strokes on the GPU-backed canvas.
    /// </summary>
    public class FaceMeshView : Control
    {
        /// <summary>Width used when the control is not otherwise constrained; height follows the frame aspect.</summary>
        private const double ReferenceWidth = 480;

        public static readonly StyledProperty<FaceMeshFrame?> FrameProperty =
            AvaloniaProperty.Register<FaceMeshView, FaceMeshFrame?>(nameof(Frame));

        private static readonly IBrush BackgroundBrush = new SolidColorBrush(Color.FromRgb(0, 0, 0));

        // Same palette as the phone page so both previews read the same way
        private static readonly IBrush OutlineBrush = new SolidColorBrush(Color.FromRgb(0x4c, 0xaf, 0x50));
        private static readonly IBrush EyesBrush = new SolidColorBrush(Color.FromRgb(0x60, 0xa5, 0xfa));
        private static readonly IBrush LipsBrush = new SolidColorBrush(Color.FromRgb(0xf8, 0x71, 0x71));
        private static readonly IBrush TessellationBrush = new SolidColorBrush(Color.FromArgb(0x59, 0x4c, 0xaf, 0x50));
        private static readonly IBrush FaceBoxBrush = new SolidColorBrush(Color.FromRgb(0x00, 0xff, 0x00));
        private static readonly IBrush NoseBrush = new SolidColorBrush(Color.FromRgb(0x22, 0xd3, 0xee));
        private static readonly IBrush IrisBrush = new SolidColorBrush(Color.FromRgb(0xfa, 0xcc, 0x15));
        private static readonly IBrush GazeBrush = new SolidColorBrush(Color.FromRgb(0xfb, 0xbf, 0x24));

        // View-window indicator: where the head points (green), where the eyes look (yellow) and
        // what actually goes to the game after the nudge (white ring)
        private static readonly IBrush IndicatorBackBrush = new SolidColorBrush(Color.FromArgb(0xb0, 0x11, 0x18, 0x27));
        private static readonly Pen IndicatorFramePen = new Pen(new SolidColorBrush(Color.FromRgb(0x4b, 0x55, 0x63)), 1);
        private static readonly Pen IndicatorCrossPen = new Pen(new SolidColorBrush(Color.FromArgb(0x60, 0x9c, 0xa3, 0xaf)), 1);
        private static readonly Pen IndicatorGazeLinePen = new Pen(GazeBrush, 1.5);
        private static readonly Pen IndicatorOutputPen = new Pen(new SolidColorBrush(Color.FromRgb(0xf9, 0xfa, 0xfb)), 1.5);
        private const double IndicatorRangeDegrees = 45;

        public static readonly StyledProperty<GazeIndicator?> IndicatorProperty =
            AvaloniaProperty.Register<FaceMeshView, GazeIndicator?>(nameof(Indicator));

        private Pen?[] _pens = new Pen?[0];
        private float[] _penWidths = new float[0];

        static FaceMeshView()
        {
            AffectsRender<FaceMeshView>(FrameProperty, IndicatorProperty);
        }

        /// <summary>Head and gaze directions for the view-window inset. Null hides the inset.</summary>
        public GazeIndicator? Indicator
        {
            get
            {
                return GetValue(IndicatorProperty);
            }
            set
            {
                SetValue(IndicatorProperty, value);
            }
        }

        /// <summary>
        /// A new frame only needs a layout pass when its aspect ratio differs from the last one;
        /// at camera rate that would otherwise be thirty relayouts a second for nothing.
        /// </summary>
        protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
        {
            base.OnPropertyChanged(change);

            if (change == null || change.Property != FrameProperty)
            {
                return;
            }

            var oldFrame = change.OldValue as FaceMeshFrame;
            var newFrame = change.NewValue as FaceMeshFrame;
            if (AspectOf(oldFrame) == AspectOf(newFrame))
            {
                return;
            }

            Debug.WriteLine($"[FaceMeshView] Frame aspect changed to {AspectOf(newFrame):F3}, relayout");
            InvalidateMeasure();
        }

        private static double AspectOf(FaceMeshFrame? frame)
        {
            return frame == null ? 3.0 / 4.0 : (double)frame.Height / frame.Width;
        }

        public FaceMeshView()
        {
            ClipToBounds = true;
        }

        /// <summary>The mesh to draw. Null draws an empty black panel.</summary>
        public FaceMeshFrame? Frame
        {
            get
            {
                return GetValue(FrameProperty);
            }
            set
            {
                SetValue(FrameProperty, value);
            }
        }

        protected override Size MeasureOverride(Size availableSize)
        {
            var aspect = AspectOf(Frame);

            var width = double.IsInfinity(availableSize.Width) ? ReferenceWidth : availableSize.Width;
            if (!double.IsNaN(Width))
            {
                width = Width;
            }

            return new Size(width, width * aspect);
        }

        public override void Render(DrawingContext context)
        {
            if (context == null)
            {
                return;
            }

            var bounds = new Rect(Bounds.Size);
            context.FillRectangle(BackgroundBrush, bounds);

            var frame = Frame;
            var strokeScale = bounds.Width / ReferenceWidth;
            if (frame == null || frame.Groups.Length == 0)
            {
                DrawIndicator(context, bounds, strokeScale);
                return;
            }

            // Letterbox the frame aspect inside the control so lines land where the camera saw them
            var scale = Math.Min(bounds.Width / frame.Width, bounds.Height / frame.Height);
            var drawW = frame.Width * scale;
            var drawH = frame.Height * scale;
            var offX = (bounds.Width - drawW) / 2;
            var offY = (bounds.Height - drawH) / 2;
            strokeScale = drawW / ReferenceWidth;

            foreach (var group in frame.Groups)
            {
                if (group == null || group.Segments.Length < 4)
                {
                    continue;
                }

                var geometry = new StreamGeometry();
                using (var g = geometry.Open())
                {
                    var s = group.Segments;
                    var count = group.SegmentCount;
                    for (int i = 0; i < count; i++)
                    {
                        var idx = i * 4;
                        var a = new Point(offX + s[idx] * drawW, offY + s[idx + 1] * drawH);
                        var b = new Point(offX + s[idx + 2] * drawW, offY + s[idx + 3] * drawH);
                        g.BeginFigure(a, false);
                        g.LineTo(b);
                        g.EndFigure(false);
                    }
                }

                context.DrawGeometry(null, GetPen(group.Style, (float)Math.Max(0.5, group.LineWidth * strokeScale)), geometry);
            }

            DrawIndicator(context, bounds, strokeScale);
        }

        /// <summary>
        /// The view-window inset in the top-right corner: a square spanning +-45 degrees each way.
        /// Green dot = head direction, yellow dot and line = where the eyes look on top of that,
        /// white ring = the direction sent to the game once the gaze nudge is applied.
        /// </summary>
        private void DrawIndicator(DrawingContext context, Rect bounds, double strokeScale)
        {
            var indicator = Indicator;
            if (context == null || indicator == null)
            {
                return;
            }

            var size = Math.Min(bounds.Width, bounds.Height) * 0.3;
            if (size < 24)
            {
                return;
            }

            var margin = 8 * strokeScale;
            var box = new Rect(bounds.Width - size - margin, margin, size, size);
            var centre = box.Center;
            var half = size / 2 - 4;

            context.FillRectangle(IndicatorBackBrush, box);
            context.DrawRectangle(null, IndicatorFramePen, box);
            context.DrawLine(IndicatorCrossPen, new Point(box.Left, centre.Y), new Point(box.Right, centre.Y));
            context.DrawLine(IndicatorCrossPen, new Point(centre.X, box.Top), new Point(centre.X, box.Bottom));

            Point Map(double yaw, double pitch)
            {
                // Looking left moves the dot left; looking up moves it up
                var x = centre.X - Math.Clamp(yaw / IndicatorRangeDegrees, -1, 1) * half;
                var y = centre.Y - Math.Clamp(pitch / IndicatorRangeDegrees, -1, 1) * half;
                return new Point(x, y);
            }

            var dot = Math.Max(2.5, 4 * strokeScale);
            var head = Map(indicator.HeadYaw, indicator.HeadPitch);

            if (indicator.HasGaze)
            {
                var look = Map(indicator.HeadYaw + indicator.GazeYaw, indicator.HeadPitch + indicator.GazePitch);
                context.DrawLine(IndicatorGazeLinePen, head, look);
                context.DrawEllipse(GazeBrush, null, look, dot, dot);

                var output = Map(indicator.HeadYaw + indicator.NudgeYaw, indicator.HeadPitch + indicator.NudgePitch);
                context.DrawEllipse(null, IndicatorOutputPen, output, dot * 1.6, dot * 1.6);
            }

            context.DrawEllipse(OutlineBrush, null, head, dot, dot);
        }

        /// <summary>
        /// Pens are cached per style and only rebuilt when the stroke width changes (a resize),
        /// so steady-state frames allocate geometry only.
        /// </summary>
        private Pen GetPen(FaceMeshStyle style, float width)
        {
            var index = (int)style;
            if (index >= _pens.Length)
            {
                Array.Resize(ref _pens, index + 1);
                Array.Resize(ref _penWidths, index + 1);
            }

            var existing = _pens[index];
            if (existing != null && Math.Abs(_penWidths[index] - width) < 0.01f)
            {
                return existing;
            }

            var pen = new Pen(BrushFor(style), width, null, PenLineCap.Round, PenLineJoin.Round);
            _pens[index] = pen;
            _penWidths[index] = width;
            Debug.WriteLine($"[FaceMeshView] Pen rebuilt for {style} at width {width:F2}");
            return pen;
        }

        private static IBrush BrushFor(FaceMeshStyle style)
        {
            switch (style)
            {
                case FaceMeshStyle.Eyes:
                    return EyesBrush;
                case FaceMeshStyle.Lips:
                    return LipsBrush;
                case FaceMeshStyle.Tessellation:
                    return TessellationBrush;
                case FaceMeshStyle.FaceBox:
                    return FaceBoxBrush;
                case FaceMeshStyle.Nose:
                    return NoseBrush;
                case FaceMeshStyle.Iris:
                    return IrisBrush;
                case FaceMeshStyle.Gaze:
                    return GazeBrush;
                default:
                    return OutlineBrush;
            }
        }
    }
}

using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;

namespace OsuScoutNew.Controls
{
    // A slider with two handles that picks a range (Avalonia has no range slider). Values snap
    // to TickFrequency and the handles stay at least MinRange apart. The track is drawn
    // directly: a grey line, the chosen range in Foreground, and a handle at each end.
    public class RangeSlider : Control
    {
        public static readonly StyledProperty<double> MinimumProperty = AvaloniaProperty.Register<RangeSlider, double>(nameof(Minimum));
        public static readonly StyledProperty<double> MaximumProperty = AvaloniaProperty.Register<RangeSlider, double>(nameof(Maximum), 10);
        public static readonly StyledProperty<double> LowerValueProperty = AvaloniaProperty.Register<RangeSlider, double>(nameof(LowerValue));
        public static readonly StyledProperty<double> UpperValueProperty = AvaloniaProperty.Register<RangeSlider, double>(nameof(UpperValue), 10);
        public static readonly StyledProperty<double> TickFrequencyProperty = AvaloniaProperty.Register<RangeSlider, double>(nameof(TickFrequency), 1);
        public static readonly StyledProperty<double> MinRangeProperty = AvaloniaProperty.Register<RangeSlider, double>(nameof(MinRange));
        public static readonly StyledProperty<IBrush> ForegroundProperty = AvaloniaProperty.Register<RangeSlider, IBrush>(nameof(Foreground), Brushes.HotPink);
        public static readonly StyledProperty<IBrush> TrackBrushProperty = AvaloniaProperty.Register<RangeSlider, IBrush>(nameof(TrackBrush), Brushes.DimGray);
        public static readonly StyledProperty<IBrush> ThumbBrushProperty = AvaloniaProperty.Register<RangeSlider, IBrush>(nameof(ThumbBrush), Brushes.LightGray);
        public static readonly StyledProperty<IBrush> ThumbHoverBrushProperty = AvaloniaProperty.Register<RangeSlider, IBrush>(nameof(ThumbHoverBrush), Brushes.White);
        public static readonly StyledProperty<IBrush> ThumbDragBrushProperty = AvaloniaProperty.Register<RangeSlider, IBrush>(nameof(ThumbDragBrush), Brushes.HotPink);

        public double Minimum { get => GetValue(MinimumProperty); set => SetValue(MinimumProperty, value); }
        public double Maximum { get => GetValue(MaximumProperty); set => SetValue(MaximumProperty, value); }
        public double LowerValue { get => GetValue(LowerValueProperty); set => SetValue(LowerValueProperty, value); }
        public double UpperValue { get => GetValue(UpperValueProperty); set => SetValue(UpperValueProperty, value); }
        public double TickFrequency { get => GetValue(TickFrequencyProperty); set => SetValue(TickFrequencyProperty, value); }
        public double MinRange { get => GetValue(MinRangeProperty); set => SetValue(MinRangeProperty, value); }
        public IBrush Foreground { get => GetValue(ForegroundProperty); set => SetValue(ForegroundProperty, value); }
        public IBrush TrackBrush { get => GetValue(TrackBrushProperty); set => SetValue(TrackBrushProperty, value); }
        public IBrush ThumbBrush { get => GetValue(ThumbBrushProperty); set => SetValue(ThumbBrushProperty, value); }
        public IBrush ThumbHoverBrush { get => GetValue(ThumbHoverBrushProperty); set => SetValue(ThumbHoverBrushProperty, value); }
        public IBrush ThumbDragBrush { get => GetValue(ThumbDragBrushProperty); set => SetValue(ThumbDragBrushProperty, value); }

        // Raised whenever either end changes, by dragging or from code.
        public event EventHandler ValueChanged;

        private const double ThumbWidth = 10, ThumbHeight = 16, TrackHeight = 4;
        private static readonly IPen ThumbPen = new Pen(new SolidColorBrush(Color.FromRgb(0x9A, 0x9A, 0x9A)), 1);

        private enum Thumb { None, Lower, Upper }
        private Thumb _dragging = Thumb.None, _hovered = Thumb.None;

        static RangeSlider()
        {
            AffectsRender<RangeSlider>(MinimumProperty, MaximumProperty, LowerValueProperty, UpperValueProperty,
                ForegroundProperty, TrackBrushProperty, ThumbBrushProperty);
            FocusableProperty.OverrideDefaultValue<RangeSlider>(false);
            CursorProperty.OverrideDefaultValue<RangeSlider>(new Cursor(StandardCursorType.SizeWestEast));
        }

        protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
        {
            base.OnPropertyChanged(change);
            if (change.Property == LowerValueProperty || change.Property == UpperValueProperty)
                ValueChanged?.Invoke(this, EventArgs.Empty);
        }

        protected override Size MeasureOverride(Size availableSize) =>
            new Size(double.IsInfinity(availableSize.Width) ? 120 : availableSize.Width, ThumbHeight + 4);

        private double Usable => Math.Max(1, Bounds.Width - ThumbWidth);

        private double XOf(double value)
        {
            double span = Maximum - Minimum;
            double t = span <= 0 ? 0 : (value - Minimum) / span;
            return ThumbWidth / 2 + Math.Clamp(t, 0, 1) * Usable;
        }

        private double ValueAt(double x)
        {
            double t = Math.Clamp((x - ThumbWidth / 2) / Usable, 0, 1);
            double value = Minimum + t * (Maximum - Minimum);
            if (TickFrequency > 0) value = Minimum + Math.Round((value - Minimum) / TickFrequency) * TickFrequency;
            return Math.Clamp(Math.Round(value, 6), Minimum, Maximum);
        }

        public override void Render(DrawingContext context)
        {
            double cy = Bounds.Height / 2;
            double lower = XOf(LowerValue), upper = XOf(UpperValue);

            context.DrawRectangle(TrackBrush, null, new Rect(ThumbWidth / 2, cy - TrackHeight / 2, Usable, TrackHeight), 2, 2);
            context.DrawRectangle(Foreground, null, new Rect(lower, cy - TrackHeight / 2, Math.Max(0, upper - lower), TrackHeight));
            DrawThumb(context, lower, cy, Thumb.Lower);
            DrawThumb(context, upper, cy, Thumb.Upper);
        }

        private void DrawThumb(DrawingContext context, double x, double cy, Thumb which)
        {
            IBrush fill = _dragging == which ? ThumbDragBrush : _hovered == which ? ThumbHoverBrush : ThumbBrush;
            context.DrawRectangle(fill, ThumbPen, new Rect(x - ThumbWidth / 2, cy - ThumbHeight / 2, ThumbWidth, ThumbHeight), 2, 2);
        }

        // The handle a pointer at x would grab: the nearer one, or when they overlap, the one
        // that can move in the direction of the pointer.
        private Thumb ThumbNear(double x)
        {
            double lower = XOf(LowerValue), upper = XOf(UpperValue);
            if (Math.Abs(upper - lower) < 1) return x < lower ? Thumb.Lower : Thumb.Upper;
            return Math.Abs(x - lower) <= Math.Abs(x - upper) ? Thumb.Lower : Thumb.Upper;
        }

        protected override void OnPointerPressed(PointerPressedEventArgs e)
        {
            base.OnPointerPressed(e);
            if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
            double x = e.GetPosition(this).X;
            _dragging = ThumbNear(x);
            e.Pointer.Capture(this);
            MoveTo(x);
            e.Handled = true;
        }

        protected override void OnPointerMoved(PointerEventArgs e)
        {
            base.OnPointerMoved(e);
            double x = e.GetPosition(this).X;
            if (_dragging != Thumb.None)
            {
                MoveTo(x);
                return;
            }
            var near = ThumbNear(x);
            double centre = near == Thumb.Lower ? XOf(LowerValue) : XOf(UpperValue);
            var hovered = Math.Abs(x - centre) <= ThumbWidth ? near : Thumb.None;
            if (hovered != _hovered)
            {
                _hovered = hovered;
                InvalidateVisual();
            }
        }

        protected override void OnPointerReleased(PointerReleasedEventArgs e)
        {
            base.OnPointerReleased(e);
            _dragging = Thumb.None;
            e.Pointer.Capture(null);
            InvalidateVisual();
        }

        protected override void OnPointerExited(PointerEventArgs e)
        {
            base.OnPointerExited(e);
            if (_hovered == Thumb.None) return;
            _hovered = Thumb.None;
            InvalidateVisual();
        }

        private void MoveTo(double x)
        {
            double value = ValueAt(x);
            if (_dragging == Thumb.Lower) LowerValue = Math.Min(value, UpperValue - MinRange);
            else if (_dragging == Thumb.Upper) UpperValue = Math.Max(value, LowerValue + MinRange);
        }
    }
}

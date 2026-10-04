using System;
using System.Windows;
using System.Windows.Controls;

namespace DownloadsOSX.Views
{
    /// <summary>
    /// Fluid grid for the stack's tile view: as many columns as fit at <see cref="MinItemWidth"/>, then every tile is
    /// stretched to an equal share of the row, so the grid always spans the full width (no ragged empty right edge).
    /// Row height is the tallest tile in that row.
    /// </summary>
    public sealed class TilePanel : Panel
    {
        public static readonly DependencyProperty MinItemWidthProperty = DependencyProperty.Register(
            nameof(MinItemWidth), typeof(double), typeof(TilePanel),
            new FrameworkPropertyMetadata(96.0, FrameworkPropertyMetadataOptions.AffectsMeasure));

        public double MinItemWidth
        {
            get => (double)GetValue(MinItemWidthProperty);
            set => SetValue(MinItemWidthProperty, value);
        }

        private int _columns = 1;
        private double _itemWidth;

        protected override Size MeasureOverride(Size available)
        {
            int count = InternalChildren.Count;
            double min = Math.Max(1, MinItemWidth);
            double width = double.IsInfinity(available.Width) ? min * Math.Max(1, Math.Min(count, 4)) : available.Width;

            _columns = Math.Max(1, (int)Math.Floor(width / min));
            _itemWidth = Math.Floor(width / _columns);

            double height = 0, rowHeight = 0;
            for (int i = 0; i < count; i++)
            {
                var child = InternalChildren[i];
                child.Measure(new Size(_itemWidth, double.PositiveInfinity));
                rowHeight = Math.Max(rowHeight, child.DesiredSize.Height);
                if ((i + 1) % _columns == 0 || i == count - 1)
                {
                    height += rowHeight;
                    rowHeight = 0;
                }
            }
            return new Size(width, height);
        }

        protected override Size ArrangeOverride(Size finalSize)
        {
            int count = InternalChildren.Count;
            double y = 0;
            for (int rowStart = 0; rowStart < count; rowStart += _columns)
            {
                int rowEnd = Math.Min(rowStart + _columns, count);
                double rowHeight = 0;
                for (int i = rowStart; i < rowEnd; i++) rowHeight = Math.Max(rowHeight, InternalChildren[i].DesiredSize.Height);
                for (int i = rowStart; i < rowEnd; i++)
                    InternalChildren[i].Arrange(new Rect((i - rowStart) * _itemWidth, y, _itemWidth, rowHeight));
                y += rowHeight;
            }
            return finalSize;
        }
    }
}

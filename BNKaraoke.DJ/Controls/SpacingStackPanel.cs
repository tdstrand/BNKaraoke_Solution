using System.Windows;
using System.Windows.Controls;

namespace BNKaraoke.DJ.Controls;

/// <summary>
/// StackPanel that offers a <see cref="Spacing"/> property similar to WinUI's implementation.
/// The control preserves any margins already set on the child elements and simply
/// adds the requested spacing in the appropriate direction.
/// </summary>
public class SpacingStackPanel : StackPanel
{
    public static readonly DependencyProperty SpacingProperty = DependencyProperty.Register(
        nameof(Spacing),
        typeof(double),
        typeof(SpacingStackPanel),
        new FrameworkPropertyMetadata(0d, FrameworkPropertyMetadataOptions.AffectsMeasure, OnSpacingChanged));

    private static readonly DependencyProperty OriginalMarginProperty = DependencyProperty.RegisterAttached(
        "OriginalMargin",
        typeof(Thickness),
        typeof(SpacingStackPanel),
        new FrameworkPropertyMetadata(default(Thickness)));

    public double Spacing
    {
        get => (double)GetValue(SpacingProperty);
        set => SetValue(SpacingProperty, value);
    }

    protected override void OnVisualChildrenChanged(DependencyObject? visualAdded, DependencyObject? visualRemoved)
    {
        if (visualRemoved is FrameworkElement removed)
        {
            removed.ClearValue(OriginalMarginProperty);
        }

        if (visualAdded is FrameworkElement added && added.ReadLocalValue(OriginalMarginProperty) == DependencyProperty.UnsetValue)
        {
            added.SetValue(OriginalMarginProperty, added.Margin);
        }

        base.OnVisualChildrenChanged(visualAdded, visualRemoved);

        UpdateChildMargins();
    }

    protected override void OnPropertyChanged(DependencyPropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);

        if (e.Property == OrientationProperty)
        {
            UpdateChildMargins();
        }
    }

    private static void OnSpacingChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is SpacingStackPanel panel)
        {
            panel.UpdateChildMargins();
        }
    }

    private void UpdateChildMargins()
    {
        var spacing = Spacing;
        var childrenCount = InternalChildren.Count;

        for (var index = 0; index < childrenCount; index++)
        {
            if (InternalChildren[index] is not FrameworkElement child)
            {
                continue;
            }

            if (child.ReadLocalValue(OriginalMarginProperty) == DependencyProperty.UnsetValue)
            {
                child.SetValue(OriginalMarginProperty, child.Margin);
            }

            var baseMargin = (Thickness)child.GetValue(OriginalMarginProperty);
            Thickness newMargin;

            if (Orientation == Orientation.Horizontal)
            {
                newMargin = new Thickness(
                    baseMargin.Left + (index == 0 ? 0 : spacing),
                    baseMargin.Top,
                    baseMargin.Right,
                    baseMargin.Bottom);
            }
            else
            {
                newMargin = new Thickness(
                    baseMargin.Left,
                    baseMargin.Top + (index == 0 ? 0 : spacing),
                    baseMargin.Right,
                    baseMargin.Bottom);
            }

            if (child.Margin != newMargin)
            {
                child.Margin = newMargin;
            }
        }
    }
}

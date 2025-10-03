using System.Windows;
using System.Windows.Controls;

namespace BNKaraoke.DJ.Views.Overlays
{
    public partial class OverlayBand : UserControl
    {
        public static readonly DependencyProperty ContentPaddingProperty = DependencyProperty.Register(
            nameof(ContentPadding),
            typeof(Thickness),
            typeof(OverlayBand),
            new PropertyMetadata(new Thickness(32, 24, 32, 24)));

        public OverlayBand()
        {
            InitializeComponent();
        }

        public Thickness ContentPadding
        {
            get => (Thickness)GetValue(ContentPaddingProperty);
            set => SetValue(ContentPaddingProperty, value);
        }
    }
}

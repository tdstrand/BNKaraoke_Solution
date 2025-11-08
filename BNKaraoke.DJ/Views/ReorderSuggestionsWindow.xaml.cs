using System.Collections.Generic;
using System.Windows;
using BNKaraoke.DJ.Models;

namespace BNKaraoke.DJ.Views
{
    public partial class ReorderSuggestionsWindow : Window
    {
        public ReorderSuggestionsWindow(List<ReorderSuggestion> suggestions)
        {
            InitializeComponent();
            DataContext = suggestions;
        }

        private void Approve_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = true;
            Close();
        }
    }
}

namespace BNKaraoke.DJ.Models
{
    public class ReorderSuggestion
    {
        public string? SingerName { get; set; }
        public int CurrentPosition { get; set; }
        public int SuggestedPosition { get; set; }
        public string? Reason { get; set; }
    }

    public class ReorderSuggestionResponse
    {
        public List<ReorderSuggestion>? Suggestions { get; set; }
    }

    public class ApplyReorderRequest
    {
        public int EventId { get; set; }
        public List<ReorderSuggestion>? Suggestions { get; set; }
    }
}

using System.Windows;
using QuickJot.ViewModels;

namespace QuickJot;

public partial class TagsWindow : Window
{
    public TagsWindow(TagsViewModel viewModel, string? theme)
    {
        InitializeComponent();
        DataContext = viewModel;

        // Появление — как у главного окна, только расти неоткуда, кроме как из центра.
        Loaded += (_, _) => Appearance.Play(Root, Appearance.FromCenter);

        Theme.Apply(this, theme);
        ThemeMode = theme switch
        {
            "light" => ThemeMode.Light,
            "dark" => ThemeMode.Dark,
            _ => ThemeMode.System,
        };
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        Native.ApplyMicaAndRoundCorners(new System.Windows.Interop.WindowInteropHelper(this).Handle);
    }

    private void OnDoneClick(object sender, RoutedEventArgs e) => Close();
}

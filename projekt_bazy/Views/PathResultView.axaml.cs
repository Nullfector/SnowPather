using System.Collections.Generic;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Interactivity;

namespace projekt_bazy.Views;

public partial class PathResultView : UserControl
{
    private readonly MainWindow _mainWindow;
    private readonly string _graphName;

    public PathResultView(MainWindow mainWindow, string graphName, List<string> edgeNames)
    {
        InitializeComponent();

        _mainWindow = mainWindow;
        _graphName = graphName;

        TitleTextBlock.Text = $"Wynik œcie¿ki dla {_graphName}";

        var formattedEdges = edgeNames
            .Select((edgeName, index) => $"{index + 1})    {edgeName}")
            .ToList();

        ResultItemsControl.ItemsSource = formattedEdges;
    }

    private void BackButton_Click(object? sender, RoutedEventArgs e)
    {
        _mainWindow.ShowPathFindingView(_graphName);
    }
}
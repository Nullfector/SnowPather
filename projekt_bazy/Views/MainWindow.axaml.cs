using Avalonia.Controls;
using System.Collections.Generic;

namespace projekt_bazy.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();

        ShowStartView();
    }

    public void ShowStartView()
    {
        MainContent.Content = new StartView(this);
    }

    public void ShowCreateGraphView()
    {
        MainContent.Content = new CreateGraphView(this);
    }

    public void ShowGraphPanelView(string graphName)
    {
        MainContent.Content = new GraphPanelView(this, graphName);
    }

    public void ShowGraphVisualizerView(string graphName) 
    { 
        MainContent.Content = new GraphVisualizerView(this, graphName);
    }

    public void ShowPathFindingView(string graphName)
    {
        MainContent.Content = new PathFindingView(this, graphName);
    }

    public void ShowPathResultView(string graphName, List<string> edgeNames)
    {
        MainContent.Content = new PathResultView(this, graphName, edgeNames);
    }
}
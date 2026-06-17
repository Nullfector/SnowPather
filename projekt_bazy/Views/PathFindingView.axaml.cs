using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Interactivity;
using projekt_bazy.Services;
using System.Collections.ObjectModel;

namespace projekt_bazy.Views;

public partial class PathFindingView : UserControl
{
    private readonly MainWindow _mainWindow;
    private readonly string _graphName;
    private readonly ObservableCollection<string> _availableImportantPlaces = new();
    private readonly ObservableCollection<string> _selectedImportantPlaces = new();

    private bool _updatingAllPriorities;

    public PathFindingView(MainWindow mainWindow, string graphName)
    {
        InitializeComponent();

        ImportantPlaceComboBox.ItemsSource = _availableImportantPlaces;
        SelectedImportantPlacesListBox.ItemsSource = _selectedImportantPlaces;
        _mainWindow = mainWindow;
        _graphName = graphName;

        TitleTextBlock.Text = $"Szukanie œcie¿ki dla {_graphName}";

        Loaded += async (_, _) => await LoadInitialDataAsync();
    }

    private async Task LoadInitialDataAsync()
    {
        try
        {
            StatusTextBlock.Text = "£adowanie danych...";

            await using var service = new Neo4jService();

            var startPoints = await service.GetStartPointsAsync(_graphName);
            var importantPlaces = await service.GetImportantPlacesAsync(_graphName);

            StartPointComboBox.ItemsSource = startPoints;

            if (startPoints.Count > 0)
                StartPointComboBox.SelectedIndex = 0;

            _availableImportantPlaces.Clear();
            _selectedImportantPlaces.Clear();

            foreach (var place in importantPlaces)
                _availableImportantPlaces.Add(place);

            if (_availableImportantPlaces.Count > 0)
                ImportantPlaceComboBox.SelectedIndex = 0;

            //StatusTextBlock.Text =
            //  $"Za³adowano punkty startowe: {startPoints.Count}, wa¿ne lokalizacje: {importantPlaces.Count}.";
            StatusTextBlock.Text ="Algorytm gotowy do dzia³ania.";
        }
        catch (Exception ex)
        {
            StatusTextBlock.Text = $"B³¹d ³adowania danych: {ex.Message}";
        }
    }

    private void AddImportantPlaceButton_Click(object? sender, RoutedEventArgs e)
    {
        if (ImportantPlaceComboBox.SelectedItem is not string place)
        {
            StatusTextBlock.Text = "Wybierz wa¿n¹ lokalizacjê do dodania.";
            return;
        }

        _availableImportantPlaces.Remove(place);
        _selectedImportantPlaces.Add(place);

        if (_availableImportantPlaces.Count > 0)
            ImportantPlaceComboBox.SelectedIndex = 0;
        else
            ImportantPlaceComboBox.SelectedItem = null;

        StatusTextBlock.Text = $"Dodano lokalizacjê: {place}.";
    }

    private void AllPrioritiesCheckBox_Click(object? sender, RoutedEventArgs e)
    {
        bool isChecked = AllPrioritiesCheckBox.IsChecked == true;

        Priority1CheckBox.IsChecked = isChecked;
        Priority2CheckBox.IsChecked = isChecked;
        Priority3CheckBox.IsChecked = isChecked;
    }

    private void RemoveImportantPlaceButton_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button button)
            return;

        if (button.DataContext is not string place)
            return;

        _selectedImportantPlaces.Remove(place);
        _availableImportantPlaces.Add(place);

        SortAvailableImportantPlaces();

        if (_availableImportantPlaces.Count > 0)
            ImportantPlaceComboBox.SelectedIndex = 0;

        StatusTextBlock.Text = $"Usuniêto lokalizacjê: {place}.";
    }

    private void SortAvailableImportantPlaces()
    {
        var sorted = _availableImportantPlaces
            .OrderBy(x => x)
            .ToList();

        _availableImportantPlaces.Clear();

        foreach (var item in sorted)
            _availableImportantPlaces.Add(item);
    }

    private async void ActivateButton_Click(object? sender, RoutedEventArgs e)
    {
        var selectedPriorities = GetSelectedPriorities();
        var importantPlaces = _selectedImportantPlaces.ToList();

        if (selectedPriorities.Count == 0)
        {
            StatusTextBlock.Text = "Wybierz przynajmniej jeden priorytet drogi.";
            return;
        }

        var algorithm = GetSelectedAlgorithm();

        if (string.IsNullOrWhiteSpace(algorithm))
        {
            StatusTextBlock.Text = "Wybierz algorytm.";
            return;
        }

        var startPoint = StartPointComboBox.SelectedItem as string;

        if (string.IsNullOrWhiteSpace(startPoint))
        {
            StatusTextBlock.Text = "Wybierz punkt startowy.";
            return;
        }

        //StatusTextBlock.Text =
        //$"Aktywacja: algorytm={algorithm}, start={startPoint}, priorytety={string.Join(", ", selectedPriorities)}, wa¿ne lokalizacje={string.Join(", ", importantPlaces)}";
        StatusTextBlock.Text = "Pracujê....";
        // Tutaj póŸniej odpalisz w³aœciwy algorytm, np.:
        // await service.RunNaivePathFindingAsync(_graphName, startPoint, selectedPriorities);

        Neo4jService serv = new Neo4jService();
        List<string>? final = await serv.NaiveAlgorithm(startPoint, selectedPriorities, importantPlaces, _graphName);
        await serv.ReturnGraph(_graphName);

        if(final != null || final.Count()!=0)
        {
            _mainWindow.ShowPathResultView(_graphName, final);
        }
        StatusTextBlock.Text = "KRYTYCZNY B£¥D - nie znaleniono œcie¿ek!";

    }

    private List<int> GetSelectedPriorities()
    {
        var priorities = new List<int>();

        if (Priority1CheckBox.IsChecked == true)
            priorities.Add(1);

        if (Priority2CheckBox.IsChecked == true)
            priorities.Add(2);

        if (Priority3CheckBox.IsChecked == true)
            priorities.Add(3);

        return priorities;
    }

    private string? GetSelectedAlgorithm()
    {
        if (AlgorithmComboBox.SelectedItem is ComboBoxItem item)
            return item.Content?.ToString();

        return AlgorithmComboBox.SelectedItem?.ToString();
    }

    private void BackButton_Click(object? sender, RoutedEventArgs e)
    {
        _mainWindow.ShowGraphPanelView(_graphName);
    }
}
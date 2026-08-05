using System.Windows;
using System.Windows.Threading;
using BleFinder.Core.Presentation;

namespace BleFinder.App;

public partial class MainWindow : Window
{
    private readonly DispatcherTimer _refreshTimer;
    private bool _isClosed;

    public MainWindow()
    {
        InitializeComponent();

        _refreshTimer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromMilliseconds(250),
        };
        _refreshTimer.Tick += OnRefreshTick;
        _refreshTimer.Start();
    }

    protected override void OnClosed(EventArgs e)
    {
        if (!_isClosed)
        {
            _isClosed = true;
            _refreshTimer.Stop();
            _refreshTimer.Tick -= OnRefreshTick;

            if (DataContext is MainViewModel viewModel)
            {
                DataContext = null;
                viewModel.Dispose();
            }
        }

        base.OnClosed(e);
    }

    private void OnRefreshTick(object? sender, EventArgs e)
    {
        if (DataContext is MainViewModel viewModel)
        {
            viewModel.Refresh();
        }
    }
}

// COPYRIGHT (c) Martin Lechner

namespace EtaEstimator.WpfDemo.ViewModels
{
    using System.ComponentModel;
    using System.Runtime.CompilerServices;
    using System.Threading.Tasks;
    using System.Windows.Threading;
    using Utils;

    public class MainViewModel : INotifyPropertyChanged
    {
        private ETAEstimator? _estimator;
        private readonly DispatcherTimer _autoTimer;
        private readonly Random _rng = new(42);

        public MainViewModel()
        {
            _autoTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(200) };
            _autoTimer.Tick += async (_, __) => await DoStepAsync();

            CreateCommand = new RelayCommand(_ => Create());
            StepCommand = new RelayCommand(async _ => await DoStepAsync());
            ToggleAutoCommand = new RelayCommand(_ => ToggleAuto());
            ResetCommand = new RelayCommand(_ => Reset());
        }

        #region Properties (bindable)

        private double _totalUnits = 100;
        public double TotalUnits { get => _totalUnits; set { _totalUnits = value; OnPropertyChanged(); } }

        private double _unitsPerStep = 1;
        public double UnitsPerStep { get => _unitsPerStep; set { _unitsPerStep = value; OnPropertyChanged(); } }

        private bool _useRandomDelay = true;
        public bool UseRandomDelay { get => _useRandomDelay; set { _useRandomDelay = value; OnPropertyChanged(); } }

        private int _fixedDelayMs = 120;
        public int FixedDelayMs { get => _fixedDelayMs; set { _fixedDelayMs = value; OnPropertyChanged(); } }

        public string DoneTotal => _estimator == null ? "" : $"{_estimator.Done:0}/{_estimator.Total:0}";
        public string ProgressText => _estimator == null ? "" : $"{_estimator.Percent:0.0}%";
        public double ProgressPercent => _estimator?.Percent ?? 0.0;
        public string EtaFormatted => _estimator == null ? "" :
            (double.IsInfinity(_estimator.GetEtaSeconds()) ? "∞" : TimeSpan.FromSeconds(_estimator.GetEtaSeconds()).ToString(@"mm\:ss"));
        public string EtaSeconds => _estimator == null ? "" :
            (double.IsInfinity(_estimator.GetEtaSeconds()) ? "∞" : _estimator.GetEtaSeconds().ToString("0.000"));

        private string _autoButtonText = "Auto ▶";
        public string AutoButtonText { get => _autoButtonText; set { _autoButtonText = value; OnPropertyChanged(); } }

        #endregion

        #region Commands
        public RelayCommand CreateCommand { get; }
        public RelayCommand StepCommand { get; }
        public RelayCommand ToggleAutoCommand { get; }
        public RelayCommand ResetCommand { get; }
        #endregion

        private void Create()
        {
            _estimator = new ETAEstimator(TotalUnits);
            RaiseAllOutputs();
        }

        private async Task DoStepAsync()
        {
            var est = _estimator;
            if (est == null) return;

            int delay = UseRandomDelay ? _rng.Next(80, 350) : FixedDelayMs;
            await Task.Delay(delay);

            if (est != _estimator) return;

            est.Step(UnitsPerStep);
            RaiseAllOutputs();

            if (est.Done >= est.Total)
            {
                _autoTimer.Stop();
                AutoButtonText = "Auto ▶";
            }
        }


        private void ToggleAuto()
        {
            if (_autoTimer.IsEnabled)
            {
                _autoTimer.Stop();
                AutoButtonText = "Auto ▶";
            }
            else
            {
                _autoTimer.Start();
                AutoButtonText = "Auto ⏸";
            }
        }

        private void Reset()
        {
            _autoTimer.Stop();
            _estimator = null;
            AutoButtonText = "Auto ▶";
            RaiseAllOutputs();
        }

        private void RaiseAllOutputs()
        {
            OnPropertyChanged(nameof(DoneTotal));
            OnPropertyChanged(nameof(ProgressText));
            OnPropertyChanged(nameof(ProgressPercent));
            OnPropertyChanged(nameof(EtaFormatted));
            OnPropertyChanged(nameof(EtaSeconds));
        }

        #region INotifyPropertyChanged
        public event PropertyChangedEventHandler? PropertyChanged;
        private void OnPropertyChanged([CallerMemberName] string? name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        #endregion
    }
}

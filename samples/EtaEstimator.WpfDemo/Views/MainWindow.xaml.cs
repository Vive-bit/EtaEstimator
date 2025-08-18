// COPYRIGHT (c) Martin Lechner

namespace EtaEstimator.WpfDemo.Views
{
    using EtaEstimator.WpfDemo.ViewModels;
    using System.Windows;

    public partial class MainWindow : Window
    {
        public MainWindow()
        {
            InitializeComponent();
            DataContext = new MainViewModel();
        }
    }
}
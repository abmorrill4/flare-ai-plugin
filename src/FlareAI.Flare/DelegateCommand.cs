using System;
using System.Windows.Input;

namespace FlareAI.Flare
{
    /// <summary>Minimal ICommand for ribbon buttons. If api-dump shows a MadCap ButtonCommand base type is required, swap here.</summary>
    public sealed class DelegateCommand : ICommand
    {
        private readonly Action _execute;
        public DelegateCommand(Action execute) { _execute = execute; }
        public bool CanExecute(object parameter) { return true; }
        public void Execute(object parameter) { _execute(); }
        public event EventHandler CanExecuteChanged { add { } remove { } }
    }
}

using System.Windows.Controls;
using GdAntiVirus.Core;

namespace GdAntiVirus.Pages
{
    /// <summary>Base for all pages: built lazily on first show.</summary>
    public abstract class PageBase : UserControl
    {
        private bool _built;

        public abstract string Header { get; }
        public abstract string Subtitle { get; }

        protected MainWindow Main => Ui.Main;

        public void Show()
        {
            if (!_built)
            {
                _built = true;
                Build();
            }
            OnShown();
        }

        protected abstract void Build();
        protected virtual void OnShown() { }

        protected bool Confirm(string title, string text, string ok = "Да", bool danger = false)
            => !AppSettings.ConfirmActions || Ui.Confirm(title, text, ok, danger);
    }
}

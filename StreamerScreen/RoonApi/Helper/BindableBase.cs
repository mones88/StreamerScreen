using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace RoonApiLib.Helper
{
    public class BindableBase : INotifyPropertyChanged
    {
        private event PropertyChangedEventHandler _PropertyChangedEvent;

        public event PropertyChangedEventHandler PropertyChanged
        {
            add
            {
                _PropertyChangedEvent += value;
            }
            remove
            {
                _PropertyChangedEvent -= value;
            }
        }

        protected void OnPropertyChanged([CallerMemberName] string propertyName = "")
        {
            if (_PropertyChangedEvent != null)
                _PropertyChangedEvent(this, new PropertyChangedEventArgs(propertyName));
        }

        protected virtual bool SetProperty<T>(ref T field, T value, [CallerMemberName] string propertyName = "")
        {
            if (!object.Equals(field, value))
            {
                field = value;
                OnPropertyChanged(propertyName);
                return true;
            }
            return false;
        }
    }
}

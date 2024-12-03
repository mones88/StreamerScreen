using System.Threading.Tasks;

namespace RoonApiLib
{
    public interface IRoonControlAdaptor
    {
        string DisplayName { get; }
        int MaxVolume   { get; }
        int Volume      { get; }
        bool Muted      { get; }
        bool Power      { get; }
        bool Selected   { get; }

        Task<bool> SetVolume    (int volume);
        Task<bool> SetMuted     (bool muted);
        Task<bool> SetPower     (bool on);
        Task<bool> Select       ();
        Task<bool> GetStatus    (int maxAgeMS);
    }
}

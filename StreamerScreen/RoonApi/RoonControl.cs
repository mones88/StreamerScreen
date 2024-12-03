using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using RoonApiLib;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace RoonApiLib
{
    public class RoonControl
    {
        RoonApi                             _api;
        RoonApiControlVolume                _apiControlVolume;
        RoonApiControlSource                _apiControlSource;
        RoonApiStatus                       _apiStatus;
        RoonApiSettings                     _apiSettings;
        Discovery.Result                    _core;
        RoonApi.RoonRegister                _roonRegister;
        RoonApiControlVolume.Volume         _volume;
        RoonApiControlSource.Source         _source;
        ILogger                             _logger;
        IRoonControlAdaptor                 _adaptor;
        string                              _coreName;
        bool                                _isOnline;
        RoonApiControlSource.EStatus        _lastStatus;
        int                                 _lastVolume;
        bool                                _lastMuted;
        CancellationTokenSource             _cancellationTokenSource;
        DateTime                            _nextDeviceCycle;
        int                                 _slowCycleMS, _fastCycleMS, _currentCycleMS;
        string                              _myIPAddress;
        public RoonControl (IRoonControlAdaptor adaptor, string myIPAddress, string coreName, int slowCycleMS, int fastCycleMS, string persistencePath, ILogger logger)
        {
            _adaptor = adaptor;
            _myIPAddress = myIPAddress;
            _coreName = coreName;
            _logger = logger;
            _api = new RoonApi(null, null, persistencePath, logger);
            _apiStatus = new RoonApiStatus(_api);
            _isOnline = false;
            _cancellationTokenSource = new CancellationTokenSource();
            _slowCycleMS = slowCycleMS;
            _fastCycleMS = fastCycleMS;
        }
        public RoonApiStatus    ApiStatus => _apiStatus;
        public RoonApiSettings  ApiSettings => _apiSettings;
        public RoonApiControlVolume ApiControlVolume => _apiControlVolume;
        public RoonApiControlSource ApiControlSource => _apiControlSource;
        public bool IsOnline => _isOnline;
        public CancellationTokenSource CancellationTokenSource => _cancellationTokenSource;

        public void AddSettings (List<RoonApiSettings.LayoutBase> layout, Dictionary<string,string> values, RoonApiSettings.Functions functions)
        {
            _apiSettings = new RoonApiSettings(_api, layout, values, functions);
        }
        public bool HasSubScriptions
        {
            get
            {
                if (_apiControlVolume == null || _apiControlSource == null)
                    return false;
                return _apiControlSource.HasSubscriptions || _apiControlVolume.HasSubscriptions;
            }
        }
        public async Task<bool> Start ()
        {

            _lastStatus = GetSourceControlStatus();
            _lastVolume = _adaptor.Volume;
            _lastMuted  = _adaptor.Muted;

            SetSlowCycle();

            _logger.LogInformation("Start RIC's Roon Controller");
            // Init Controls
            _apiControlVolume = new RoonApiControlVolume(_api, false);
            _volume = new RoonApiControlVolume.Volume
            {
                DisplayName = _adaptor.DisplayName + " Volume",
                VolumeMax = _adaptor.MaxVolume,
                VolumeStep = 1,
                VolumeType = "number",
                VolumeValue = _adaptor.Volume,
                IsMuted = _adaptor.Muted
            };
            _apiControlVolume.AddControl(_volume, new RoonApiControlVolume.VolumeFunctions
            {
                SetVolume = async (arg) => {
                    _logger.LogTrace($"SETVOLUME {arg.Mode} {arg.Value}"); 
                    await _adaptor.SetVolume(arg.Value);
                    SetFastCycle();
                    return true;
                },
                Mute = async (arg) => {
                    _logger.LogTrace($"MUTE {arg.Mute} ");
                    await _adaptor.SetMuted(arg.Mute == RoonApiTransport.EMute.mute );
                    SetFastCycle();
                    return true;
                }
            });

            _apiControlSource = new RoonApiControlSource(_api, false);
            _source = new RoonApiControlSource.Source
            {
                DisplayName = _adaptor.DisplayName + " Source",
                SupportsStandBy = true,
                Status = GetSourceControlStatus ()
            };
            _apiControlSource.AddControl(_source, new RoonApiControlSource.SourceFunctions
            {
                SetStandby = async (arg) => {
                    _logger.LogTrace($"SET STANDBY {arg.Status}");
                    await _adaptor.SetPower(false);
                    SetSlowCycle();
                    return true;
                },
                SetConvenience = async (arg) => {
                    _logger.LogTrace($"SETCONVENIENCE");
                    if (!_adaptor.Power)
                        await _adaptor.SetPower(true);
                    if (_adaptor.Selected)
                        await _adaptor.Select();
                    SetFastCycle();
                    return true;
                }
            });

            // Init Service Registration
            List<string> services = new List<string>(new string[] { RoonApi.ServiceStatus, RoonApi.ControlVolume, RoonApi.ControlSource });
            if (_apiSettings != null)
                services.Add(RoonApi.ServiceSettings);
            _roonRegister = new RoonApi.RoonRegister
            {
                DisplayName = "Ric's Roon Controller",
                DisplayVersion = "1.0.0",
                Publisher = "Christian Riedl",
                Email = "ric@rts.co.at",
                WebSite = "https://github.com/christian-riedl/roon-control",
                ExtensionId = "com.ric.controller",
                Token = null,
                OptionalServices = new string[0],
                RequiredServices = new string[0],
                ProvidedServices = services.ToArray()
            };

            Discovery discovery = new Discovery(_myIPAddress, 1000, _logger);
            var coreList = await discovery.QueryServiceId((res) => {
                if (res.CoreName == _coreName)
                {
                    _core = res;
                    return true;
                }
                return false;
            });

            _api.StartReceiver(_core.CoreIPAddress, _core.HttpPort, _roonRegister);
            return true;
        }
        void SetFastCycle ()
        {
            _currentCycleMS = _fastCycleMS;
            DateTime nextDeviceCycle = DateTime.UtcNow.AddMilliseconds(_fastCycleMS);
            if (nextDeviceCycle < _nextDeviceCycle)
                _nextDeviceCycle = nextDeviceCycle;
        }
        void SetSlowCycle ()
        {
            _currentCycleMS = _slowCycleMS;
            _nextDeviceCycle = DateTime.UtcNow.AddMilliseconds(_slowCycleMS);
        }

        public async Task ProcessDeviceChangesLoop ()
        {
            while (!_cancellationTokenSource.Token.IsCancellationRequested)
            {
                await ProcessDevicesChanges();
                await Task.Delay(_fastCycleMS);
            }
        }
        public async Task ProcessDevicesChanges (bool sendStatus = true)
        {
            if (DateTime.UtcNow < _nextDeviceCycle)
                return;

            _nextDeviceCycle = DateTime.UtcNow.AddMilliseconds(_currentCycleMS);

            if (await _adaptor.GetStatus(_fastCycleMS))
            {
                if (sendStatus && !_isOnline)
                {
                    await _apiStatus.SetStatus($"{_adaptor.DisplayName} OK", false);
                }
                if (_adaptor.Muted != _lastMuted || _adaptor.Volume != _lastVolume)
                {
                    _lastMuted = _volume.IsMuted = _adaptor.Muted;
                    _lastVolume = _volume.VolumeValue = _adaptor.Volume;
                    _logger.LogTrace($"UPDATEVOLUME volume {_volume.VolumeValue} mute{_volume.IsMuted}");
                    await _apiControlVolume.UpdateState(_volume);
                }
                RoonApiControlSource.EStatus newStatus = GetSourceControlStatus();
                if (newStatus != _lastStatus)
                {
                    _lastStatus = _source.Status = newStatus;
                    _logger.LogTrace($"UPDATESTATUS {newStatus}");
                    await _apiControlSource.UpdateState(_source);
                }
                _isOnline = true;
            }
            else
            {
                if (sendStatus && _isOnline)
                {
                    await _apiStatus.SetStatus($"{_adaptor.DisplayName} FAILED", true);
                }
                _isOnline = false;
            }

        }
        public void Close ()
        {
            _logger.LogTrace("Closing");
            _cancellationTokenSource.Cancel();
            _logger.LogTrace("Closed");
        }
        RoonApiControlSource.EStatus GetSourceControlStatus ()
        {
            return _adaptor.Power ? (_adaptor.Selected ? RoonApiControlSource.EStatus.selected : RoonApiControlSource.EStatus.deselected) : RoonApiControlSource.EStatus.standby;
        }
    }
}

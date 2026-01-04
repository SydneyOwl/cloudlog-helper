#if WINDOWS
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using CloudlogHelper.Enums;
using CloudlogHelper.Models;
using CloudlogHelper.Resources;
using CloudlogHelper.Services.Interfaces;
using NLog;

namespace CloudlogHelper.Services;

public class OmniRigService : IRigService, IDisposable
{
    private static readonly Logger ClassLogger = LogManager.GetCurrentClassLogger();
    private dynamic? _omniRigEngine;
    private dynamic? _omniRig;

    private int _version = 0x00;
    
    private static readonly Mutex _mutex = new Mutex();
    
    public RigBackendServiceEnum GetServiceType()
    {
        return RigBackendServiceEnum.OmniRig;
    }
    
    public async Task StartService(CancellationToken token, params object[] args)
    {
        try
        {
            _mutex.WaitOne();
            ClassLogger.Info("Starting OmniRig");
            if (_omniRigEngine is null)
            {
                var omniRigType = Type.GetTypeFromProgID(DefaultConfigs.OmniRigEngineProgId);
                if (omniRigType is null) throw new Exception("OmniRig COM not found!");

                await Task.Run(() =>
                {
                    _omniRigEngine = Activator.CreateInstance(omniRigType);
                    if (_omniRigEngine is null) throw new Exception("Failed to create OmniRig instance!");
                }, token);

                _version = _omniRigEngine.InterfaceVersion;

                if (_version < 0x101 && _version > 0x299)
                {
                    _omniRigEngine = null;
                    throw new Exception("OmniRig is not installed or has unsupported version.");
                }
            }

            ReleaseComObject(ref _omniRig);

            _omniRig = (string)args[0] switch
            {
                "Rig 1" => _omniRigEngine.Rig1,
                "Rig 2" => _omniRigEngine.Rig2,
                _ => throw new ArgumentOutOfRangeException("RigNo", "Rig no should be 1 and 2")
            };
        }
        finally
        {
            _mutex.ReleaseMutex();
        }
       
    }

    public async Task StopService(CancellationToken token)
    {
        try
        {
            _mutex.WaitOne();
            ClassLogger.Info("Stopping OmniRig");
            await Task.Run(ReleaseUnmanagedResources, token);
        }
        finally
        {
            _mutex.ReleaseMutex();
        }
    }

    public bool IsServiceRunning()
    {
        return _omniRigEngine is not null;
    }
    
    public Task<RigInfo[]> GetSupportedRigModels()
    {
        return Task.FromResult(Array.Empty<RigInfo>());
    }

    public Task<string> GetServiceVersion(params object[] args)
    {
        return Task.FromResult(_version.ToString());
    }

    public Task<RadioData> GetAllRigInfo(bool reportRfPower, bool reportSplitInfo, 
        CancellationToken token, params object[] args)
    {
        if (_omniRig is null) throw new Exception("OmniRig not connected!");
        
        var rigName = _omniRig.RigType.ToString();
        var freq = (long)(_omniRig.Freq);
        var mode = (int)(_omniRig.Mode);
        var status = (int)(_omniRig.Status);
        
        if (status != StOnline)
        {
            if (status == StPortbusy) throw new Exception("Rig port busy!");
            if (status == StDisabled) throw new Exception("Rig is disabled!");
            if (status == StNotconfigured) throw new Exception("Rig is not configured!");
            if (status == StNotresponding) throw new Exception("Rig not responding!");
            throw new Exception("Unknown rig status!");
        }

        var modeStr = mode switch
        {
            PmCwL => "CW",
            PmCwU => "CW-R",
            PmSsbL => "LSB",
            PmSsbU => "USB",
            PmDigU => "USB-D",
            PmDigL => "LSB-D",
            PmFM => "FM",
            PmAm => "AM",
            _ => "Other"
        };

        // for now we dont support split and pwr.
        return Task.FromResult(new RadioData()
        {
            RigName = rigName,
            IsSplit = false,
            FrequencyTx = freq,
            ModeTx = modeStr,
            FrequencyRx = freq,
            ModeRx = modeStr,
            Power = null
        });
    }
    
    private void ReleaseComObject(ref object? comObject)
    {
        if (comObject is null || !Marshal.IsComObject(comObject)) return;
        try
        {
            Marshal.FinalReleaseComObject(comObject);
        }
        catch (Exception ex)
        {
            ClassLogger.Warn("Failed to release COM object!");
        }
        finally
        {
            comObject = null;
        }
    }

    public dynamic? GetRawRig()
    {
        return _omniRig;
    }
    
    private void ReleaseUnmanagedResources()
    {
        ReleaseComObject(ref _omniRig);
        ReleaseComObject(ref _omniRigEngine);
    }

    public void Dispose()
    {
        ReleaseUnmanagedResources();
        GC.SuppressFinalize(this);
    }

    ~OmniRigService()
    {
        ReleaseUnmanagedResources();
    }
    
  
    
    #region Constants

    // Constants for enum RigParamX
    private const int PmUnknown = 0x00000001;
    private const int PmFreq = 0x00000002;
    private const int PmFreqa = 0x00000004;
    private const int PmFreqb = 0x00000008;
    private const int PmPitch = 0x00000010;
    private const int PmRitoffset = 0x00000020;
    private const int PmRit0 = 0x00000040;
    private const int PmVfoaa = 0x00000080;
    private const int PmVfoab = 0x00000100;
    private const int PmVfoba = 0x00000200;
    private const int PmVfobb = 0x00000400;
    private const int PmVfoa = 0x00000800;
    private const int PmVfob = 0x00001000;
    private const int PmVfoequal = 0x00002000;
    private const int PmVfoswap = 0x00004000;
    private const int PmSpliton = 0x00008000;
    private const int PmSplitoff = 0x00010000;
    private const int PmRiton = 0x00020000;
    private const int PmRitoff = 0x00040000;
    private const int PmXiton = 0x00080000;
    private const int PmXitoff = 0x00100000;
    private const int PmRx = 0x00200000;
    private const int PmTx = 0x00400000;
    private const int PmCwU = 0x00800000;
    private const int PmCwL = 0x01000000;
    private const int PmSsbU = 0x02000000;
    private const int PmSsbL = 0x04000000;
    private const int PmDigU = 0x08000000;
    private const int PmDigL = 0x10000000;
    private const int PmAm = 0x20000000;
    private const int PmFM = 0x40000000;

    // Constants for enum RigStatusX
    private const int StNotconfigured = 0x00000000;
    private const int StDisabled = 0x00000001;
    private const int StPortbusy = 0x00000002;
    private const int StNotresponding = 0x00000003;
    private const int StOnline = 0x00000004;

    #endregion
}

#endif
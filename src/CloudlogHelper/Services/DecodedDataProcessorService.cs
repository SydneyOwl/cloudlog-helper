using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Reactive.Linq;
using CloudlogHelper.Database;
using CloudlogHelper.Models;
using CloudlogHelper.Services.Interfaces;
using CloudlogHelper.Utils;
using NLog;
using ReactiveUI;
using WsjtxUtilsPatch.WsjtxMessages.Messages;

namespace CloudlogHelper.Services;

public class DecodedDataProcessorService:IDecodedDataProcessorService,IDisposable
{
    /// <summary>
    ///     Logger for the class.
    /// </summary>
    private static readonly Logger ClassLogger = LogManager.GetCurrentClassLogger();
    private Dictionary<string, int> _callsignDistance = new();

    private ObservableCollection<Decode> _decodedCache = new();
    
    private IDatabaseService  _databaseService;

    public DecodedDataProcessorService(IDatabaseService databaseService)
    {
        _databaseService = databaseService;
        this.WhenAnyValue(x => x._callsignDistance)
            .Throttle(TimeSpan.FromSeconds(3))
            .ObserveOn(RxApp.TaskpoolScheduler)
            .Subscribe(_ =>
            {
                try
                {
                    var decodes = _decodedCache.ToArray();
                    _decodedCache.Clear();
                    _saveCallsignGridInfo(decodes);
                }
                catch (Exception ex)
                {
                    ClassLogger.Error(ex,"Error while processing decoded data...");
                }
            });
    }

    public void ProcessDecoded(Decode decode)
    {
        _decodedCache.Add(decode);
    }

    private void _cacheChartData(Decode[] decode)
    {
        foreach (var tmp in decode)
        {
            var chartQsoPoint = new ChartQSOPoint();
            var callsign = WsjtxMessageUtil.ExtractDeFromMessage(tmp.Message);
            var grid = WsjtxMessageUtil.ExtractGridFromMessage(tmp.Message);
          
            if (callsign is null) continue;
            chartQsoPoint.DxCallsign = callsign;
            if (grid is not null)
            {
                // todo
                // MaidenheadGridUtil.CalculateBearing()
            }
        }
    }

    private void _saveCallsignGridInfo(Decode[] decodes)
    {
        var collectedGrid = new List<CollectedGridDatabase>();
        foreach (var decMsg in decodes)
        {
            var call = WsjtxMessageUtil.ExtractDeFromMessage(decMsg.Message);
            var grid = WsjtxMessageUtil.ExtractGridFromMessage(decMsg.Message);
            if (call is not null && grid is not null)
            {
                collectedGrid.Add(new CollectedGridDatabase()
                {
                    Callsign = call,
                    GridSquare = grid
                });
            }
        }
        
        _databaseService.BatchAddOrUpdateCallsignGrid(collectedGrid);
        ClassLogger.Info($"Added {collectedGrid.Count} grids.");
    }

    public void Dispose()
    {
        // TODO release managed resources here
    }
}
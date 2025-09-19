using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using CloudlogHelper.Enums;
using CloudlogHelper.Models;
using CloudlogHelper.Resources;
using CloudlogHelper.Utils;

namespace CloudlogHelper.Services.Interfaces;

public interface IRigBackendManager
{
    Task InitializeAsync();

    RigBackendServiceEnum GetServiceType();

    bool IsServiceRunning();
    
    Task RestartService();

    Task<List<RigInfo>> GetSupportedRigModels();

    Task<RadioData> GetAllRigInfo();

    Task<string> GetServiceVersion();

    Task ExecuteTest(RigBackendServiceEnum backendServiceEnum,
        ApplicationSettings draftSettings);
}
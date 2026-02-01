using System;
using System.Threading.Tasks;
using CloudlogHelper.Models;
using CloudlogHelper.Services.Interfaces;
using Google.Protobuf;

namespace CloudlogHelper.Services;

public class DummyCLHServerService : ICLHServerService, IDisposable
{
    public void Dispose()
    {
        // TODO release managed resources here
    }

    public Task ReconnectAsync()
    {
        return Task.CompletedTask;
    }

    public Task SendData(IMessage data)
    {
        return Task.CompletedTask;
    }

    public Task SendDataNoException(IMessage data)
    {
        return Task.CompletedTask;
    }

    public Task DisconnectAsync()
    {
        return Task.CompletedTask;
    }

    public Task TestConnectionAsync(ApplicationSettings draftSetting, bool useTestMode = false)
    {
        return Task.CompletedTask;
    }
}
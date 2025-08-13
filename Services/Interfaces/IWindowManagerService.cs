using System;
using System.Threading.Tasks;
using Avalonia.Controls;

namespace CloudlogHelper.Services.Interfaces;

public interface IWindowManagerService
{
    void Track(Window window);
    bool TryGetWindow(Type wType, out Window? targetWindow);
    
    Task CreateOrShowWindowByVm(Type wType);
}
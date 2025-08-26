using System;
using System.Threading.Tasks;
using Avalonia.Controls;

namespace CloudlogHelper.Services.Interfaces;

public interface IWindowManagerService
{
    void Track(Window window);

    Task<T?> CreateAndShowWindowByVm<T>(Type wType, Window? toplevel = null);
    
    Task CreateAndShowWindowByVm(Type wType, Window? toplevel = null);

    public T GetViewModelInstance<T>();
}
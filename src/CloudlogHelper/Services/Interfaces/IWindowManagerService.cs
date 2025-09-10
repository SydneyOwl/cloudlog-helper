using System;
using System.Threading.Tasks;
using Avalonia.Controls;

namespace CloudlogHelper.Services.Interfaces;

public interface IWindowManagerService
{
    string Track(Window window);

    Task<T?> CreateAndShowWindowByVm<T>(Type wType, Window? toplevel = null, bool dialog = true);
    
    Task CreateAndShowWindowByVm(Type wType, Window? toplevel = null, bool dialog = true);

    public T GetViewModelInstance<T>();

    public void CloseWindowBySeq(string seq);
}
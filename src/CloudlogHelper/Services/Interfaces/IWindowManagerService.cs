using System;
using System.Threading.Tasks;
using Avalonia.Controls;

namespace CloudlogHelper.Services.Interfaces;

public interface IWindowManagerService
{
    string Track(Window window);

    Task<T?> CreateAndShowWindowByVm<T>(Type wType, Window? toplevel = null, bool dialog = true);

    Task CreateAndShowWindowByVm(Type wType, Window? toplevel = null, bool dialog = true);

    T GetViewModelInstance<T>();

    void CloseWindowBySeq(string seq);

    Task LaunchBrowser(string uri, Window? topLevel = null);
    Task LaunchDir(string path, Window? topLevel = null);
}
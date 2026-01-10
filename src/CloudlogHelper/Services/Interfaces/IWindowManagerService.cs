using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Platform.Storage;

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

    Task<IReadOnlyList<IStorageFile?>> OpenFilePickerAsync(FilePickerOpenOptions options, Window? topLevel = null);

    Task<IStorageFile?> OpenFileSaverAsync(FilePickerSaveOptions options, Window? topLevel = null);
}
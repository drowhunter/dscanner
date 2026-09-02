namespace DirectInputWatcher;

internal interface IUsbDeviceChangeSource
{
    IObservable<UsbDeviceChangeNotification> Observe();
}

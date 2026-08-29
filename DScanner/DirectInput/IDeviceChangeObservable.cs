namespace DScanner.DirectInput;

public interface IDeviceChangeObservable
{
    IObservable<DeviceChangeNotification> Observe();
}

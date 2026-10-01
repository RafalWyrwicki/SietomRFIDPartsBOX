using System.ComponentModel;
using System.Runtime.InteropServices;

namespace PartsBox;
public static class CardReaderPresence
{
 [StructLayout(LayoutKind.Sequential)] struct Device { public IntPtr Handle; public uint Type; }
 [DllImport("user32.dll",SetLastError=true)] static extern uint GetRawInputDeviceList(IntPtr list,ref uint count,uint size);
 [DllImport("user32.dll",CharSet=CharSet.Unicode,SetLastError=true)] static extern uint GetRawInputDeviceInfoW(IntPtr handle,uint command,IntPtr data,ref uint size);
 public static bool IsPresent(string identifier)
 {
  if(string.IsNullOrWhiteSpace(identifier))return false;
  for(var attempt=0;attempt<3;attempt++)
  {
   uint count=0;var size=Marshal.SizeOf<Device>();
   if(GetRawInputDeviceList(IntPtr.Zero,ref count,(uint)size)==uint.MaxValue)throw new Win32Exception();
   if(count==0)return false;
   var buffer=Marshal.AllocHGlobal(checked((int)count*size));
   try
   {
    var actual=GetRawInputDeviceList(buffer,ref count,(uint)size);
    if(actual==uint.MaxValue){if(Marshal.GetLastWin32Error()==122)continue;throw new Win32Exception();}
    for(var i=0;i<actual;i++)
    {
     var device=Marshal.PtrToStructure<Device>(buffer+i*size);if(device.Type!=1)continue;
     uint chars=0;if(GetRawInputDeviceInfoW(device.Handle,0x20000007,IntPtr.Zero,ref chars)==uint.MaxValue)continue;
     var name=Marshal.AllocHGlobal(checked(((int)chars+1)*2));
     try{if(GetRawInputDeviceInfoW(device.Handle,0x20000007,name,ref chars)!=uint.MaxValue&&(Marshal.PtrToStringUni(name)??"").Contains(identifier,StringComparison.OrdinalIgnoreCase))return true;}
     finally{Marshal.FreeHGlobal(name);}
    }
    return false;
   }
   finally{Marshal.FreeHGlobal(buffer);}
  }
  throw new InvalidOperationException("Lista urządzeń USB zmieniła się podczas sprawdzania.");
 }
}

using System.Runtime.InteropServices;
using System.Windows.Interop;

namespace PartsBox;
// Accept only keyboard reports from the configured USB reader, not typed/pasted text.
sealed class CardKeyboardInput : IDisposable
{
 [StructLayout(LayoutKind.Sequential)] struct Registration { public ushort Page,Usage;public uint Flags;public IntPtr Target; }
 [StructLayout(LayoutKind.Sequential)] struct Header {public uint Type,Size;public IntPtr Device,Parameter;}
 [StructLayout(LayoutKind.Sequential)] struct Keyboard {public ushort MakeCode,Flags,Reserved,VKey;public uint Message,Extra;}
 [DllImport("user32.dll",SetLastError=true)] static extern bool RegisterRawInputDevices(Registration[] devices,uint count,uint size);
 [DllImport("user32.dll")] static extern uint GetRawInputData(IntPtr handle,uint command,IntPtr data,ref uint size,uint headerSize);
 [DllImport("user32.dll",CharSet=CharSet.Unicode)] static extern uint GetRawInputDeviceInfoW(IntPtr device,uint command,IntPtr data,ref uint size);
 readonly HwndSource source;readonly string identifier;readonly Action<int> received;readonly Action<int,string>? otherKeyboard;
 public CardKeyboardInput(IntPtr window,string identifier,Action<int> received,Action<int,string>? otherKeyboard=null)
 {
  this.identifier=identifier;this.received=received;this.otherKeyboard=otherKeyboard;source=HwndSource.FromHwnd(window)!;
  if(!RegisterRawInputDevices([new(){Page=1,Usage=6,Target=window}],1,(uint)Marshal.SizeOf<Registration>()))throw new System.ComponentModel.Win32Exception();
  source.AddHook(Hook);
 }
 IntPtr Hook(IntPtr hwnd,int message,IntPtr w,IntPtr l,ref bool handled)
 {
  if(message!=0xFF)return IntPtr.Zero;
  uint size=0;var headerSize=(uint)Marshal.SizeOf<Header>();
  if(GetRawInputData(l,0x10000003,IntPtr.Zero,ref size,headerSize)==uint.MaxValue||size<headerSize+Marshal.SizeOf<Keyboard>())return IntPtr.Zero;
  var buffer=Marshal.AllocHGlobal((int)size);
  try
  {
   if(GetRawInputData(l,0x10000003,buffer,ref size,headerSize)==uint.MaxValue)return IntPtr.Zero;
   var header=Marshal.PtrToStructure<Header>(buffer);if(header.Type!=1)return IntPtr.Zero;
   uint chars=0;if(GetRawInputDeviceInfoW(header.Device,0x20000007,IntPtr.Zero,ref chars)==uint.MaxValue)return IntPtr.Zero;
   var name=Marshal.AllocHGlobal(checked(((int)chars+1)*2));string deviceName;
   try
   {
    if(GetRawInputDeviceInfoW(header.Device,0x20000007,name,ref chars)==uint.MaxValue)return IntPtr.Zero;
    deviceName=Marshal.PtrToStringUni(name)??"";
   }
   finally{Marshal.FreeHGlobal(name);}
   var key=Marshal.PtrToStructure<Keyboard>(buffer+(int)headerSize);
   if((key.Flags&1)==0){
    var automatic=string.Equals(identifier,"AUTO",StringComparison.OrdinalIgnoreCase);
    if(automatic||deviceName.Contains(identifier,StringComparison.OrdinalIgnoreCase))received(key.VKey);
    if(automatic||!deviceName.Contains(identifier,StringComparison.OrdinalIgnoreCase))otherKeyboard?.Invoke(key.VKey,deviceName);
   }
  }
  finally{Marshal.FreeHGlobal(buffer);}
  return IntPtr.Zero;
 }
 public void Dispose(){source.RemoveHook(Hook);RegisterRawInputDevices([new(){Page=1,Usage=6,Flags=1,Target=IntPtr.Zero}],1,(uint)Marshal.SizeOf<Registration>());}
}

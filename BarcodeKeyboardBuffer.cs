namespace PartsBox;
// A scan is one device's sequence, terminated by Enter or Tab. Never submit a partial scan on timeout.
sealed class BarcodeKeyboardBuffer
{
 readonly Dictionary<string,(string Text,long Time,bool Invalid)> buffers=new();
 public void Clear()=>buffers.Clear();
 public string? Feed(string device,int key,long time)
 {
  if(key is 16 or 160 or 161)return null; // Shift does not change hexadecimal EPC.
  buffers.TryGetValue(device,out var state);
  if(time-state.Time>1000)state=("",time,false);
  var value=state.Text??"";
  if(key is 13 or 9)
  {
   buffers.Remove(device);
   if(value.Length==0&&!state.Invalid)return null;
   return state.Invalid?"[nieprawidłowy kod]":value;
  }
  char? digit=key is >=48 and <=57 or >=65 and <=70?(char)key:key is >=96 and <=105?(char)('0'+key-96):null;
  buffers[device]=(digit!=null&&value.Length<128?value+digit:value,time,state.Invalid||digit==null||value.Length>=128);
  return null;
 }
}

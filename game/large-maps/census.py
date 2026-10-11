"""Read-only census of the verified 1.3.7.2 registry in a running local test."""
import argparse, collections, ctypes, json, struct
from ctypes import wintypes as W
p=argparse.ArgumentParser();p.add_argument('--pid',type=int,required=True);p.add_argument('--image',type=lambda x:int(x,0),required=True);p.add_argument('--state',type=lambda x:int(x,0));p.add_argument('--counts-only',action='store_true');a=p.parse_args()
k=ctypes.WinDLL('kernel32',use_last_error=True)
k.OpenProcess.argtypes=[W.DWORD,W.BOOL,W.DWORD];k.OpenProcess.restype=W.HANDLE
k.ReadProcessMemory.argtypes=[W.HANDLE,ctypes.c_void_p,ctypes.c_void_p,ctypes.c_size_t,ctypes.POINTER(ctypes.c_size_t)];k.ReadProcessMemory.restype=W.BOOL
k.CloseHandle.argtypes=[W.HANDLE]
h=k.OpenProcess(0x410,False,a.pid)
if not h:raise ctypes.WinError(ctypes.get_last_error())
def read(addr,n):
 b=ctypes.create_string_buffer(n);got=ctypes.c_size_t()
 if not k.ReadProcessMemory(h,addr,b,n,ctypes.byref(got)) or got.value!=n:raise OSError(hex(addr))
 return b.raw
def u(addr):return struct.unpack('<I',read(addr,4))[0]
try:
 if read(a.image,2)!=b'MZ':raise ValueError('Invalid image')
 reg=u(a.image+0x5ef72c);body=read(reg,0x60014)
 count,next_slot,high=struct.unpack_from('<III',body,0x60004)
 slots=struct.unpack_from('<65536I',body,0x20004);types=collections.Counter();names={};errors=0;extra={}
 if a.state:
  mode,ext_count,ext_next,ext_high,ext_errors=struct.unpack('<5I',read(a.state+8,20))
  extra=dict(mode=mode,objects=ext_count,next=ext_next,high=ext_high,errors=ext_errors)
  if not a.counts_only:slots+=struct.unpack('<196608I',read(a.state+32+196608*2,196608*4))
 if a.counts_only:slots=[]
 for obj in slots:
  if not obj:continue
  try:
   vt=u(obj)
   if vt not in names:
    try:
     locator=u(vt-4);desc=u(locator+12);name=read(desc+8,180).split(b'\0')[0].decode('ascii','replace')
    except OSError:name='unknown'
    names[vt]=name
   types[(hex(vt-a.image),names[vt])]+=1
  except OSError:errors+=1
 print(json.dumps(dict(pid=a.pid,image=hex(a.image),registry=hex(reg),count=count,next=next_slot,high=high,errors=errors,extended=extra,types=[dict(vtable=v,name=n,count=c) for (v,n),c in types.most_common()]),indent=2))
finally:k.CloseHandle(h)

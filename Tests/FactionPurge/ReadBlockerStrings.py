import ctypes as C
from ctypes import wintypes as W
import json
from pathlib import Path
pid=51128
k=C.WinDLL('kernel32',use_last_error=True)
k.OpenProcess.restype=W.HANDLE;k.OpenProcess.argtypes=[W.DWORD,W.BOOL,W.DWORD]
k.CloseHandle.argtypes=[W.HANDLE]
k.QueryFullProcessImageNameW.argtypes=[W.HANDLE,W.DWORD,W.LPWSTR,C.POINTER(W.DWORD)]
k.VirtualQueryEx.restype=C.c_size_t
class MBI(C.Structure):
 _fields_=[('BaseAddress',C.c_void_p),('AllocationBase',C.c_void_p),('AllocationProtect',W.DWORD),('PartitionId',W.WORD),('RegionSize',C.c_size_t),('State',W.DWORD),('Protect',W.DWORD),('Type',W.DWORD)]
k.VirtualQueryEx.argtypes=[W.HANDLE,C.c_void_p,C.POINTER(MBI),C.c_size_t]
k.ReadProcessMemory.argtypes=[W.HANDLE,C.c_void_p,C.c_void_p,C.c_size_t,C.POINTER(C.c_size_t)]
h=k.OpenProcess(0x410,False,pid)
if not h:raise OSError(C.get_last_error())
try:
 buf=C.create_unicode_buffer(32768);n=W.DWORD(len(buf));assert k.QueryFullProcessImageNameW(h,0,buf,C.byref(n))
 assert buf.value.lower()==r'C:\RWPurge0913\Full03\Game\RimWorldWin64.exe'.lower(),buf.value
 prefixes=[v.encode('utf-16-le') for v in ['第三方组件仍引用目标，需要专门适配：','目标派系的人物或财产仍在保留地图 ','目标地图有其他玩家的人物或俘虏，请先撤离：','目标地图有其他玩家的财产：','目标人物仍在其他地图，请先撤回：','目标人物在运输或特殊容器中：','请先处理远行队、运输器或特殊据点：','关联任务包含第三方 QuestPart，需先结束并清理：','存在未结束的 MP 交易、仪式或编队会话；请先关闭后重试。']]
 found=set();addr=0;chunk=C.create_string_buffer(4*1024*1024);total=0
 while True:
  info=MBI()
  if not k.VirtualQueryEx(h,C.c_void_p(addr),C.byref(info),C.sizeof(info)):break
  end=(info.BaseAddress or 0)+info.RegionSize
  if end<=addr:break
  if info.State==0x1000 and not(info.Protect&0x101) and (info.Protect&0xff) in [2,4,8,0x20,0x40,0x80]:
   offset=info.BaseAddress or 0;carry=b''
   while offset<end:
    size=min(len(chunk),end-offset);got=C.c_size_t()
    if k.ReadProcessMemory(h,C.c_void_p(offset),chunk,size,C.byref(got)):
     data=carry+chunk.raw[:got.value];pos=0;total+=got.value
     for prefix in prefixes:
      pos=0
      while True:
       pos=data.find(prefix,pos)
       if pos<0:break
       if pos>=4:
        length=int.from_bytes(data[pos-4:pos],'little')
        if len(prefix)//2<=length<=2048:
         text=data[pos:pos+length*2].decode('utf-16-le',errors='replace')
         if len(text)==length:found.add(text)
       pos+=len(prefix)
     carry=data[-2048:]
    offset+=size
  addr=end
 result={'pid':pid,'executable':buf.value,'bytesRead':total,'blockerComponentStrings':sorted(found)}
 Path(r'C:\RWPurge0913\Full03\read-only-all-blockers.json').write_text(json.dumps(result,ensure_ascii=False,indent=2),encoding='utf-8')
 print(json.dumps(result,ensure_ascii=True,indent=2))
finally:k.CloseHandle(h)

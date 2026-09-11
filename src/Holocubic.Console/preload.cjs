const {contextBridge,ipcRenderer,webUtils}=require('electron');
const call=(method,data)=>ipcRenderer.invoke('desktop-call',method,data).then(r=>{if(!r.ok)throw Error(r.error);return r.value});
contextBridge.exposeInMainWorld('desktop',{
 call,
 uploadDropped:(files,data)=>call('filesUpload',{...data,sources:files.map(file=>webUtils.getPathForFile(file)).filter(Boolean)}),
 windowAction:action=>ipcRenderer.send('window-action',action),
 onEvent:callback=>ipcRenderer.on('desktop-event',(_event,data)=>callback(data)),
 onWindowState:callback=>ipcRenderer.on('window-state',(_event,value)=>callback(value))
});

'use strict';
const path=require('node:path');
const {translate}=require('./locales.js');
const {Files,join,entryName,remotePath}=require('./files.cjs');
function createFiles({BrowserWindow,dialog,shell,window,emit,dataDir,prefs}){
 const manager=new Files({dialog,shell,window,emit,tempDir:path.join(dataDir,'file-transfer-cache')});
 const editors=new Map();
 const address=d=>d.address||prefs().active;
 async function open(d){const ip=address(d),p=remotePath(d.path);for(const {win,data} of editors.values())if(data.address===ip&&data.path===p&&!win.isDestroyed()){win.show();win.focus();return true;}const data={...await manager.read(ip,p),address:ip,uiLanguage:prefs().uiLanguage||'zh-CN'};const win=new BrowserWindow({parent:window(),width:980,height:740,minWidth:640,minHeight:420,show:false,autoHideMenuBar:true,title:path.posix.basename(p)+' · '+translate('文件编辑',prefs().uiLanguage)+' · '+translate('holocubic控制台',prefs().uiLanguage),webPreferences:{preload:path.join(__dirname,'preload.cjs'),contextIsolation:true,nodeIntegration:false,sandbox:true}});const entry={win,data,dirty:false,closing:false};const editorId=win.webContents.id;editors.set(editorId,entry);win.webContents.setWindowOpenHandler(()=>({action:'deny'}));win.webContents.on('will-navigate',e=>e.preventDefault());win.on('close',e=>{if(!entry.dirty)return;e.preventDefault();if(entry.closing)return;entry.closing=true;dialog.showMessageBox(win,{type:'question',message:'文件尚未保存，放弃修改并关闭？',buttons:['继续编辑','放弃修改'],defaultId:0,cancelId:0}).then(r=>{entry.closing=false;if(r.response===1){entry.dirty=false;win.close();}});});win.on('closed',()=>editors.delete(editorId));await win.loadFile(path.join(__dirname,'file-editor.html'));win.show();return true;}
 const handlers={
  filesList:d=>manager.list(address(d),d.path),
  filesMkdir:async d=>{if(manager.active())throw Error('请等待传输队列结束后修改目录');const p=join(d.directory,d.name);if(await manager.stat(address(d),p))throw Error('同名项目已存在');await manager.ensureDir(address(d),p);return true;},
  filesRename:async d=>{if(manager.active())throw Error('请等待传输队列结束后重命名');const p=remotePath(d.path);const to=join(path.posix.dirname(p),entryName(d.name));if(to===p)return true;if(await manager.stat(address(d),to))throw Error('同名项目已存在');return manager.rename(address(d),p,to);},
  filesDelete:d=>manager.confirmDelete(address(d),d.paths),
  filesUpload:d=>manager.chooseUpload(address(d),d.directory,d.folder,d.sources),
  filesDownload:d=>manager.chooseDownload(address(d),d.paths),
  filesPaste:d=>manager.paste(address(d),d.paths,d.directory,d.move),
  filesTransfers:()=>manager.snapshot(),filesCancel:d=>manager.cancel(d.id),filesRetry:d=>manager.retry(d.id),filesReveal:d=>manager.reveal(d.id),filesOpen:open
 };
 async function editorCall(event,method,d){const entry=editors.get(event.sender.id);if(!entry||event.senderFrame!==entry.win.webContents.mainFrame)return undefined;try{let value;if(method==='fileEditorInit')value=entry.data;else if(method==='fileEditorDirty'){entry.dirty=!!d.dirty;value=true;}else if(method==='fileEditorSave'){value=await manager.saveText(entry.data.address,d.path,String(d.text),d.hash);if(value){entry.data={...entry.data,...value,text:String(d.text)};entry.dirty=false;entry.win.setTitle(path.posix.basename(value.path)+' · '+translate('文件编辑',prefs().uiLanguage)+' · '+translate('holocubic控制台',prefs().uiLanguage));emit('files-changed',{address:entry.data.address});}}else throw Error('编辑窗口不支持此操作');return {ok:true,value};}catch(e){return {ok:false,error:translate(e.message,prefs().uiLanguage)};}}
 async function confirmClose(){const dirty=[...editors.values()].filter(e=>e.dirty);if(!manager.active()&&!dirty.length)return true;const r=await dialog.showMessageBox(window(),{type:'warning',message:'关闭控制台？',detail:[manager.active()?'进行中的文件传输将取消。':'',dirty.length?dirty.length+' 个文件的未保存修改将丢弃。':''].filter(Boolean).join('\n'),buttons:['继续使用','关闭'],defaultId:0,cancelId:0});if(r.response!==1)return false;for(const e of dirty)e.dirty=false;manager.close();return true;}
 return {manager,handlers,editorCall,confirmClose,setLanguage:language=>{for(const e of editors.values()){e.data.uiLanguage=language;e.win.webContents.send('desktop-event',{type:'ui-language',value:language});}},hasPending:()=>manager.active()||[...editors.values()].some(e=>e.dirty)};
}
module.exports={createFiles};

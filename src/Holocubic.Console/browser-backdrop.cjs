'use strict';
function createBrowserBackdrop({getView,isVisible,setVisible,setBounds,emit}){
 let sequence=0;
 return async({bounds,visible,preserve=false})=>{
  const current=++sequence,view=getView();if(bounds&&['x','y','width','height'].every(k=>Number.isFinite(bounds[k])))setBounds(bounds);
  if(!visible&&preserve&&view&&isVisible()&&!view.webContents.isDestroyed()){
   const contents=view.webContents,url=contents.getURL();let timeout;
   try{const screenshot=await Promise.race([contents.capturePage(),new Promise(resolve=>{timeout=setTimeout(()=>resolve(null),500);})]);
    if(current===sequence&&view===getView()&&!contents.isDestroyed()&&url===contents.getURL()&&screenshot&&!screenshot.isEmpty())emit('browser-backdrop',{image:screenshot.toDataURL()});
   }catch{}finally{clearTimeout(timeout);}
  }
  if(current===sequence&&view===getView())setVisible(!!visible);return true;
 };
}
module.exports={createBrowserBackdrop};

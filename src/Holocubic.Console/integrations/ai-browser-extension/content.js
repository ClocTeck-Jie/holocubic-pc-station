'use strict';
(()=>{
 const provider=HoloProviders.find(location.hostname);if(!provider)return;
 const stopText=/^(stop( generating| generation| response| responding)?|停止(生成|回答|响应|回复|輸出|输出)?|中止|生成を停止|回答を停止|antwort stoppen|generierung stoppen)$/i;
 const sendText=/^(send( message| prompt)?|submit|发送(消息)?|發送(訊息)?|送信|senden)$/i;
 const visible=e=>e&&e.getClientRects().length>0&&getComputedStyle(e).visibility!=='hidden';
 const matches=(el,selectors)=>selectors.some(s=>el.matches(s));
 const label=el=>(el.getAttribute('aria-label')||el.getAttribute('title')||el.innerText||'').trim().slice(0,80);
 const isStop=el=>matches(el,provider.stop)||stopText.test(label(el));
 const isSend=el=>!el.disabled&&(matches(el,provider.send)||sendText.test(label(el)));
 let lastUrl=location.href,scheduled=false,lastBeat=0,enabled=true,networkUntil=0;
 const networkActive=new Set();
 const send=event=>chrome.runtime.sendMessage({kind:'activity',provider:provider.id,...event}).catch(()=>{});
 const detector=new HoloDetector(send);
 window.addEventListener('message',event=>{const data=event.data;if(event.source!==window||event.origin!==location.origin||data?.channel!=='holopet-ai-lifecycle-v1'||typeof data.id!=='string'||data.id.length>80||!['start','done','stop','error'].includes(data.state))return;
  networkUntil=Date.now()+3000;
  if(data.state==='start'){if(networkActive.size<100)networkActive.add(data.id);if(enabled)detector.submit()}
  else{networkActive.delete(data.id);if(networkActive.size||!enabled)return;detector.active=false;detector.generating=false;detector.suppress=true;send({state:data.state==='done'?'done':data.state==='error'?'error':'idle',event:data.state==='done'?'Stop':data.state==='error'?'GenerationError':'Interrupt'})}
 });
 const scan=()=>{scheduled=false;if(location.href!==lastUrl){if(!networkActive.size)detector.reset();lastUrl=location.href}if(!enabled)return;
  if(networkActive.size||Date.now()<networkUntil){if(networkActive.size&&Date.now()-lastBeat>12000){lastBeat=Date.now();send({state:'thinking',event:'Heartbeat'})}return}
  // Inspect controls only. No conversation text, prompt contents, URLs or titles are transmitted.
  const buttons=[...document.querySelectorAll('button,[role="button"]')].filter(visible);
  const generating=buttons.some(isStop);
  const error=detector.active&&[...document.querySelectorAll('[role="alert"]')].some(el=>visible(el)&&/something went wrong|error generating|出了点问题|生成.{0,5}(错误|失败)|稍后重试/i.test(el.innerText||''));
  detector.observe(generating,error);
  if(detector.active&&Date.now()-lastBeat>12000){lastBeat=Date.now();send({state:'thinking',event:'Heartbeat'})}
 };
 const schedule=()=>{if(!scheduled){scheduled=true;setTimeout(scan,250)}};
 document.addEventListener('click',event=>{const el=event.target.closest('button,[role="button"]');if(!enabled||!el)return;if(isStop(el)){detector.stop();return}if(isSend(el))detector.submit()},true);
 document.addEventListener('submit',()=>{if(enabled)detector.submit()},true);
 document.addEventListener('keydown',event=>{if(!enabled||event.key!=='Enter'||event.shiftKey||event.ctrlKey||event.altKey||event.isComposing)return;const target=event.target;if(!target.matches('textarea,[contenteditable="true"],[role="textbox"]'))return;const form=target.closest('form')||target.parentElement?.parentElement;if(form&&[...form.querySelectorAll('button,[role="button"]')].some(isSend))detector.submit()},true);
 new MutationObserver(schedule).observe(document.documentElement,{subtree:true,childList:true,attributes:true,attributeFilter:['aria-label','disabled','class','data-state']});
 chrome.storage.local.get({enabled:true}).then(v=>{enabled=v.enabled;scan()});
 chrome.storage.onChanged.addListener((changes,area)=>{if(area==='local'&&changes.enabled){enabled=changes.enabled.newValue;if(!enabled)detector.reset();else scan()}});
 setInterval(scan,1500);
 chrome.runtime.sendMessage({kind:'available',provider:provider.id}).catch(()=>{});
})();

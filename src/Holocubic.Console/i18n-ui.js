'use strict';
(()=>{
 let language='zh-CN';const titleSource=document.title;
 // Translate only console-owned copy; device data and file contents retain their original text.
 const excluded='script,style,textarea,pre,[data-no-i18n],#activeDeviceName,#activeDeviceIp,#sideIp,#sideDeviceName,.hero-copy h2,.row-title,.service-row>b,.scan-result b,.network-copy strong,.fm-name,.fm-tree-row [data-fm-path]:not([data-fm-path="/sd"]),.fm-current [data-filename],#fmCrumbs [data-fm-path]:not([data-fm-path="/sd"]),.device-option b,.page-head p,.fm-item-status span,#filename,#filepath,.browser-address,#startup option:not([value=""]),#timezone';
 const textSources=new WeakMap(),attributeSources=new WeakMap();
 const t=value=>HoloLocale.translate(value,language);
 function text(node){if(node.parentElement?.closest(excluded))return;const old=textSources.get(node),current=node.nodeValue;const source=old&&current===old.rendered?old.source:current;const rendered=t(source);textSources.set(node,{source,rendered});if(current!==rendered)node.nodeValue=rendered;}
 function attributes(el){if(el.closest('[data-no-i18n]'))return;let sources=attributeSources.get(el);if(!sources){sources={};attributeSources.set(el,sources);}for(const name of ['title','aria-label','placeholder']){if(!el.hasAttribute(name))continue;const current=el.getAttribute(name),old=sources[name],source=old&&current===old.rendered?old.source:current,rendered=t(source);sources[name]={source,rendered};if(rendered!==current)el.setAttribute(name,rendered);}}
 function walk(root){if(root.nodeType===Node.TEXT_NODE){text(root);return;}if(root.nodeType!==Node.ELEMENT_NODE&&root.nodeType!==Node.DOCUMENT_NODE)return;if(root.nodeType===Node.ELEMENT_NODE)attributes(root);const walker=document.createTreeWalker(root,NodeFilter.SHOW_TEXT);let n;while((n=walker.nextNode()))text(n);root.querySelectorAll('[title],[aria-label],[placeholder]').forEach(attributes);}
 function setLanguage(value){language=HoloLocale.codes.includes(value)?value:'zh-CN';document.documentElement.lang=language;document.querySelectorAll('[data-ui-language]').forEach(el=>el.value=language);walk(document.body);document.title=t(titleSource);document.querySelectorAll('.fm-list-area').forEach(el=>el.dataset.dropLabel=t('松开以开始上传'));}
 const observer=new MutationObserver(records=>{for(const r of records){if(r.type==='characterData')text(r.target);else if(r.type==='attributes')attributes(r.target);else for(const node of r.addedNodes)walk(node);}document.querySelectorAll('[data-ui-language]').forEach(el=>{if(el.value!==language)el.value=language;});document.querySelectorAll('.fm-list-area').forEach(el=>el.dataset.dropLabel=t('松开以开始上传'));});
 observer.observe(document.body,{subtree:true,childList:true,characterData:true,attributes:true,attributeFilter:['title','aria-label','placeholder']});
 document.addEventListener('change',async e=>{if(!e.target.matches('[data-ui-language]'))return;const select=e.target,old=language,next=select.value;select.disabled=true;try{await window.desktop.call('setUiLanguage',{language:next});if(typeof state!=='undefined')state.prefs.uiLanguage=next;setLanguage(next);}catch(error){select.value=old;if(typeof toast==='function')toast(error.message);}finally{select.disabled=false;}},true);
 window.desktop.onEvent(({type,value})=>{if(type==='ui-language')setLanguage(value);});
 window.HoloI18n={setLanguage,translate:t,get language(){return language;}};
 setLanguage(language);
})();

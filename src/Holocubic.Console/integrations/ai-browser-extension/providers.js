(function(root){
 const providers=[
  {id:'chatgpt',name:'ChatGPT',hosts:['chatgpt.com','chat.openai.com'],stop:['[data-testid="stop-button"]','[data-testid="stop-generating-button"]'],send:['[data-testid="send-button"]']},
  {id:'claude',name:'Claude',hosts:['claude.ai'],stop:['button[aria-label="Stop response"]'],send:['button[aria-label="Send message"]']},
  {id:'gemini',name:'Gemini',hosts:['gemini.google.com'],stop:['button[aria-label="Stop response"]','button[aria-label="停止回答"]'],send:['button.send-button:not(.stop)']},
  {id:'deepseek',name:'DeepSeek',hosts:['chat.deepseek.com'],stop:[],send:[]},
  {id:'kimi',name:'Kimi',hosts:['www.kimi.com','kimi.com','kimi.moonshot.cn'],stop:['.stop-button','.stop-btn'],send:['.send-button','.send-btn']},
  {id:'doubao',name:'豆包',hosts:['www.doubao.com'],stop:['[data-testid="chat_input_local_break_button"]'],send:['[data-testid="chat_input_send_button"]']},
  {id:'qwen',name:'通义千问',hosts:['chat.qwen.ai','tongyi.aliyun.com','www.qianwen.com'],stop:[],send:['#send-message-button']},
  {id:'grok',name:'Grok',hosts:['grok.com'],stop:['button[aria-label="Stop generating"]'],send:['button[aria-label="Send message"]']}
 ];
 const api={providers,find:host=>providers.find(p=>p.hosts.includes(host))};if(typeof module==='object')module.exports=api;else root.HoloProviders=api;
})(globalThis);

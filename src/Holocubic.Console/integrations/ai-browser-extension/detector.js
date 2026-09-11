(function(root){
 class Detector{
  constructor(emit,now=()=>Date.now()){this.emit=emit;this.now=now;this.active=false;this.generating=false;this.pendingAt=0;this.endAt=0;this.last='';}
  send(state,event){const key=state+event;if(this.last===key)return;this.last=key;this.emit({state,event})}
  submit(){this.active=true;this.generating=false;this.pendingAt=this.now();this.endAt=0;this.last='';this.send('thinking','UserPromptSubmit')}
  stop(){if(!this.active)return;this.active=false;this.generating=false;this.endAt=0;this.suppress=true;this.send('idle','Interrupt')}
  observe(generating,error){
   if(this.suppress){if(!generating)this.suppress=false;return}
   if(error&&this.active){this.active=false;this.generating=false;this.send('error','GenerationError');return}
   if(generating){if(!this.active){this.active=true;this.last=''}this.generating=true;this.endAt=0;this.send('thinking','Generating');return}
   if(!this.active)return;
   if(this.generating){this.endAt||=this.now();if(this.now()-this.endAt>=1500){this.active=false;this.generating=false;this.send('done','Stop')}}
   else if(this.now()-this.pendingAt>30000){this.active=false;this.send('idle','Unrecognized')}
  }
  reset(){if(this.active)this.send('idle','Navigation');this.active=false;this.generating=false;this.endAt=0;this.last=''}
 }
 if(typeof module==='object')module.exports={Detector};else root.HoloDetector=Detector;
})(globalThis);

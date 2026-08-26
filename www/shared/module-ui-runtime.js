(function(root,factory){
  'use strict';
  var api=factory(root);
  if(typeof module==='object'&&module.exports){module.exports=api;return;}
  Object.defineProperty(root,'SaagarUiFoundation',{value:api,enumerable:true,writable:false,configurable:false});
})(typeof window!=='undefined'?window:globalThis,function(root){
  'use strict';
  var MODES=Object.freeze(['auto','mobile','compact','tablet','desktop']);
  function classifyWidth(width){
    var value=Number(width);
    if(!Number.isFinite(value)||value<0)throw new TypeError('width must be a non-negative finite number');
    if(value<640)return 'mobile';
    if(value<900)return 'compact';
    if(value<1200)return 'tablet';
    return 'desktop';
  }
  function assertRoot(element){
    if(!element||typeof element.setAttribute!=='function'||typeof element.removeAttribute!=='function')throw new TypeError('root must be an element');
  }
  function configure(element,options){
    assertRoot(element);
    var mode=options&&options.mode!==undefined?String(options.mode):'auto';
    if(MODES.indexOf(mode)<0)throw new TypeError('unsupported width mode: '+mode);
    var previous={
      ui:element.getAttribute('data-saagar-ui'),
      mode:element.getAttribute('data-saagar-width'),
      resolved:element.getAttribute('data-saagar-width-resolved')
    };
    var active=true;
    function viewportWidth(){
      if(options&&typeof options.getWidth==='function')return options.getWidth();
      if(root&&Number.isFinite(root.innerWidth))return root.innerWidth;
      if(element.clientWidth!==undefined)return element.clientWidth;
      throw new TypeError('no viewport width is available');
    }
    function refresh(){
      if(!active)return null;
      var resolved=mode==='auto'?classifyWidth(viewportWidth()):mode;
      element.setAttribute('data-saagar-width-resolved',resolved);
      return resolved;
    }
    function restore(name,value){if(value===null)element.removeAttribute(name);else element.setAttribute(name,value);}
    function destroy(){
      if(!active)return;
      active=false;
      if(root&&typeof root.removeEventListener==='function')root.removeEventListener('resize',refresh);
      restore('data-saagar-ui',previous.ui);
      restore('data-saagar-width',previous.mode);
      restore('data-saagar-width-resolved',previous.resolved);
    }
    element.setAttribute('data-saagar-ui','');
    element.setAttribute('data-saagar-width',mode);
    if(mode==='auto'&&root&&typeof root.addEventListener==='function')root.addEventListener('resize',refresh);
    refresh();
    return Object.freeze({mode:mode,refresh:refresh,destroy:destroy});
  }
  return Object.freeze({version:1,modes:MODES,classifyWidth:classifyWidth,configure:configure});
});

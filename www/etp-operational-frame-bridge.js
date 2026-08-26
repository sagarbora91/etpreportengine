/* V6 ETP: closure-bound same-origin frame access to the parent operational composer. */
(function(root){'use strict';if(root.SaagarEtpOperationalFrameBridge)return;var parentWindow=null;try{parentWindow=root.parent&&root.parent!==root?root.parent:null;}catch(_){}
  function compose(options){var composer;try{composer=parentWindow&&parentWindow.SaagarEtpOperationalShellComposer;}catch(_){composer=null;}if(!composer||typeof composer.compose!=='function')return Promise.resolve(Object.freeze({ok:false,code:'ETP_FRAME_COMPOSER_UNAVAILABLE'}));options=options&&typeof options==='object'?options:{};return composer.compose({root:parentWindow,roots:options.roots,getScope:options.getScope});}
  Object.defineProperty(root,'SaagarEtpOperationalFrameBridge',{value:Object.freeze({version:1,compose:compose}),enumerable:true,writable:false,configurable:false});
})(window);

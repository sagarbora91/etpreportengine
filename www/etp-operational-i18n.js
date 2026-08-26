/* V6 ETP operational UI localization. Hindi/Marathi entries are TEST_ONLY_UNAPPROVED
   until native-language staff UAT approves them. Business data is never translated. */
(function(root,factory){'use strict';var api=factory(root);if(typeof module==='object'&&module.exports)module.exports=api;if(root){root.SaagarEtpOperationalI18n=api;if(root.document){var boot=function(){root.SaagarEtpOperationalI18nController=api.autoBoot(root.document,root);};if(root.document.readyState==='loading'&&typeof root.document.addEventListener==='function')root.document.addEventListener('DOMContentLoaded',boot,{once:true});else boot();}}})(typeof globalThis!=='undefined'?globalThis:this,function(root){
  'use strict';
  var VERSION='ETP_OPERATIONAL_I18N_V1',STATUS='TEST_ONLY_UNAPPROVED',LANGUAGES=Object.freeze(['en','mr','hi']);
  var PHRASES=Object.freeze([
    ['Operational ETP unavailable','ऑपरेशनल ETP अनुपलब्ध','ऑपरेशनल ETP अनुपलब्ध'],
    ['E3 · CRO reconciliation','E3 · CRO सामंजस्य','E3 · CRO मिलान'],
    ['E4 target planning ready','E4 लक्ष्य नियोजन तयार','E4 लक्ष्य योजना तैयार'],
    ['E4 target planning unavailable','E4 लक्ष्य नियोजन अनुपलब्ध','E4 लक्ष्य योजना अनुपलब्ध'],
    ['E6 exception monitoring ready','E6 अपवाद निरीक्षण तयार','E6 अपवाद निगरानी तैयार'],
    ['E6 exception monitoring blocked','E6 अपवाद निरीक्षण अवरोधित','E6 अपवाद निगरानी अवरुद्ध'],
    ['E5 incentive control ready','E5 प्रोत्साहन नियंत्रण तयार','E5 प्रोत्साहन नियंत्रण तैयार'],
    ['E5 incentive control blocked','E5 प्रोत्साहन नियंत्रण अवरोधित','E5 प्रोत्साहन नियंत्रण अवरुद्ध'],
    ['E7 Service ETP verification ready','E7 सेवा ETP पडताळणी तयार','E7 सेवा ETP सत्यापन तैयार'],
    ['E7 Service ETP ready for first verification','E7 सेवा ETP पहिल्या पडताळणीसाठी तयार','E7 सेवा ETP पहले सत्यापन के लिए तैयार'],
    ['E7 Service verification BLOCKED','E7 सेवा पडताळणी अवरोधित','E7 सेवा सत्यापन अवरुद्ध'],
    ['E7 Service verification BLOCKED / deferred','E7 सेवा पडताळणी अवरोधित / पुढे ढकललेली','E7 सेवा सत्यापन अवरुद्ध / स्थगित'],
    ['Select a day to begin.','सुरू करण्यासाठी दिवस निवडा.','शुरू करने के लिए दिन चुनें।'],
    ['Load selected day','निवडलेला दिवस लोड करा','चुना हुआ दिन लोड करें'],
    ['Open day','दिवस उघडा','दिन खोलें'],
    ['Close declarations','घोषणा बंद करा','घोषणाएँ बंद करें'],
    ['Import verified facts','पडताळलेली तथ्ये आयात करा','सत्यापित तथ्य आयात करें'],
    ['Run reconciliation','सामंजस्य चालवा','मिलान चलाएँ'],
    ['Confirm variance','फरक निश्चित करा','अंतर की पुष्टि करें'],
    ['Confirm reconciled','सामंजस्य निश्चित करा','मिलान की पुष्टि करें'],
    ['Lock day','दिवस लॉक करा','दिन लॉक करें'],
    ['Start restatement cycle','पुनर्निवेदन चक्र सुरू करा','पुनर्कथन चक्र शुरू करें'],
    ['Declarations','घोषणा','घोषणाएँ'],
    ['Invoice ID','इनव्हॉइस ID','इनवॉइस ID'],
    ['CRO ID','CRO ID','CRO ID'],
    ['Declared net amount','घोषित निव्वळ रक्कम','घोषित शुद्ध राशि'],
    ['Add declaration','घोषणा जोडा','घोषणा जोड़ें'],
    ['Reconciliation outcomes','सामंजस्य परिणाम','मिलान परिणाम'],
    ['No reconciliation outcomes yet.','अद्याप सामंजस्य परिणाम नाहीत.','अभी कोई मिलान परिणाम नहीं है।'],
    ['Correction and disposition','दुरुस्ती आणि निपटारा','सुधार और निपटान'],
    ['Correct CRO ID','योग्य CRO ID','सही CRO ID'],
    ['Correction reason','दुरुस्तीचे कारण','सुधार का कारण'],
    ['Apply correction','दुरुस्ती लागू करा','सुधार लागू करें'],
    ['Disposition code','निपटारा कोड','निपटान कोड'],
    ['Disposition reason','निपटाऱ्याचे कारण','निपटान का कारण'],
    ['Record disposition','निपटारा नोंदवा','निपटान दर्ज करें'],
    ['Unassigned queue','न नेमलेली रांग','असाइन न की गई कतार'],
    ['No unassigned invoices.','न नेमलेली इनव्हॉइस नाहीत.','कोई अनअसाइन इनवॉइस नहीं है।'],
    ['Audit trail','ऑडिट मागोवा','ऑडिट ट्रेल'],
    ['No actions recorded.','कोणतीही कृती नोंदलेली नाही.','कोई कार्रवाई दर्ज नहीं है।'],
    ['Authority and source readiness','अधिकार आणि स्रोत तयारी','प्राधिकरण और स्रोत तैयारी'],
    ['Immutable version history','अपरिवर्तनीय आवृत्ती इतिहास','अपरिवर्तनीय संस्करण इतिहास'],
    ['Verified pace, gap, run-rate and landing','पडताळलेली गती, तूट, रन-रेट आणि अंदाज','सत्यापित गति, अंतर, रन-रेट और अनुमान'],
    ['Approved Leave records','मंजूर रजा नोंदी','स्वीकृत अवकाश रिकॉर्ड'],
    ['Coverage Shortfall','कव्हरेज तूट','कवरेज की कमी'],
    ['Publish target plan','लक्ष्य योजना प्रकाशित करा','लक्ष्य योजना प्रकाशित करें'],
    ['Revise target plan','लक्ष्य योजना सुधारित करा','लक्ष्य योजना संशोधित करें'],
    ['Reallocate targets','लक्ष्यांचे पुनर्वाटप करा','लक्ष्य पुनः आवंटित करें'],
    ['Exception summary','अपवाद सारांश','अपवाद सारांश'],
    ['Refresh exceptions','अपवाद रीफ्रेश करा','अपवाद रीफ्रेश करें'],
    ['Acknowledge','स्वीकार नोंदवा','स्वीकार करें'],
    ['Reassign','पुन्हा नेमा','पुनः असाइन करें'],
    ['Add evidence','पुरावा जोडा','साक्ष्य जोड़ें'],
    ['Close with evidence','पुराव्यासह बंद करा','साक्ष्य के साथ बंद करें'],
    ['Authority and verified period','अधिकार आणि पडताळलेला कालावधी','प्राधिकरण और सत्यापित अवधि'],
    ['Eligibility and controlled actions','पात्रता आणि नियंत्रित कृती','पात्रता और नियंत्रित कार्रवाइयाँ'],
    ['Calculate incentive','प्रोत्साहन मोजा','प्रोत्साहन की गणना करें'],
    ['Publish to controlled Payroll bridge','नियंत्रित पेरोल ब्रिजवर प्रकाशित करा','नियंत्रित पेरोल ब्रिज पर प्रकाशित करें'],
    ['Calculate clawback','क्लॉबॅक मोजा','क्लॉबैक की गणना करें'],
    ['Service authority and verified source','सेवा अधिकार आणि पडताळलेला स्रोत','सेवा प्राधिकरण और सत्यापित स्रोत'],
    ['Run controlled S003 ↔ S004 verification','नियंत्रित S003 ↔ S004 पडताळणी चालवा','नियंत्रित S003 ↔ S004 सत्यापन चलाएँ'],
    ['Exact-key results and discrepancies','अचूक-कुंजी परिणाम आणि विसंगती','सटीक-कुंजी परिणाम और विसंगतियाँ'],
    ['No Service ETP verification has been recorded for this exact source binding.','या अचूक स्रोत बंधासाठी सेवा ETP पडताळणी नोंदलेली नाही.','इस सटीक स्रोत बाइंडिंग के लिए कोई सेवा ETP सत्यापन दर्ज नहीं है।'],
    ['Immutable verification and closure history','अपरिवर्तनीय पडताळणी आणि बंद इतिहास','अपरिवर्तनीय सत्यापन और समापन इतिहास'],
    ['No history recorded.','कोणताही इतिहास नोंदलेला नाही.','कोई इतिहास दर्ज नहीं है।'],
    ['Working…','काम सुरू आहे…','काम जारी है…'],
    ['Loading E4 target planning','E4 लक्ष्य नियोजन लोड होत आहे','E4 लक्ष्य योजना लोड हो रही है'],
    ['Loading E6 exceptions','E6 अपवाद लोड होत आहेत','E6 अपवाद लोड हो रहे हैं'],
    ['Loading E5 incentive control','E5 प्रोत्साहन नियंत्रण लोड होत आहे','E5 प्रोत्साहन नियंत्रण लोड हो रहा है'],
    ['Loading E7 Service verification','E7 सेवा पडताळणी लोड होत आहे','E7 सेवा सत्यापन लोड हो रहा है']
  ]);
  var lookup=null,originals=typeof WeakMap==='function'?new WeakMap():null;
  function language(value){return LANGUAGES.indexOf(value)>=0?value:'en';}
  function dictionaries(){if(lookup)return lookup;lookup={mr:Object.create(null),hi:Object.create(null)};for(var i=0;i<PHRASES.length;i++){lookup.mr[PHRASES[i][0]]=PHRASES[i][1];lookup.hi[PHRASES[i][0]]=PHRASES[i][2];}return lookup;}
  function translate(value,lang){if(typeof value!=='string')return value;lang=language(lang);if(lang==='en')return value;var match=/^(\s*)([\s\S]*?)(\s*)$/.exec(value),core=match[2],next=dictionaries()[lang][core];return next===undefined?value:match[1]+next+match[3];}
  function english(value){var match=/^(\s*)([\s\S]*?)(\s*)$/.exec(value),core=match[2];for(var i=0;i<PHRASES.length;i++)if(core===PHRASES[i][1]||core===PHRASES[i][2])return match[1]+PHRASES[i][0]+match[3];return value;}
  function excluded(node){var tag=String(node&&node.tagName||'').toUpperCase();if(/^(?:INPUT|TEXTAREA|SELECT|OPTION|TBODY)$/.test(tag))return true;if(node&&typeof node.getAttribute==='function'&&(node.getAttribute('data-no-i18n')!==null||node.getAttribute('contenteditable')==='true'))return true;return false;}
  function textNode(node,lang){var current=node.nodeValue;if(typeof current!=='string')return;if(originals&&!originals.has(node))originals.set(node,english(current));var source=originals?originals.get(node):english(current),next=lang==='en'?source:translate(source,lang);if(current!==next)node.nodeValue=next;}
  function leaf(node,lang){var current=node.textContent;if(typeof current!=='string')return;if(originals&&!originals.has(node))originals.set(node,english(current));var source=originals?originals.get(node):english(current),next=lang==='en'?source:translate(source,lang);if(current!==next)node.textContent=next;}
  function walk(node,lang){if(!node)return;if(node.nodeType===3){textNode(node,lang);return;}if(excluded(node))return;var children=node.childNodes||node.children||[];if(children.length){for(var i=0;i<children.length;i++)walk(children[i],lang);}else leaf(node,lang);}
  function apply(surface,lang){lang=language(lang);if(!surface)return Object.freeze({ok:false,code:'ETP_I18N_SURFACE_REQUIRED'});walk(surface,lang);if(typeof surface.setAttribute==='function'){surface.setAttribute('data-etp-i18n-surface','');surface.setAttribute('lang',lang);}return Object.freeze({ok:true,language:lang,status:STATUS});}
  function attach(surface,options){if(!surface)return Object.freeze({ok:false,code:'ETP_I18N_SURFACE_REQUIRED'});options=options&&typeof options==='object'?options:{};var current=language(typeof options.getLanguage==='function'?options.getLanguage():'en'),observer=null,destroyed=false;function refresh(next){if(destroyed)return Object.freeze({ok:false,code:'ETP_I18N_DESTROYED'});current=language(next===undefined?current:next);return apply(surface,current);}var Observer=options.MutationObserver||(root&&root.MutationObserver);if(typeof Observer==='function'){observer=new Observer(function(){refresh();});observer.observe(surface,{childList:true,subtree:true,characterData:true});}refresh();return Object.freeze({ok:true,controller:Object.freeze({refresh:refresh,destroy:function(){if(destroyed)return;destroyed=true;if(observer)observer.disconnect();}})});}
  function autoBoot(doc,win){if(!doc||typeof doc.querySelectorAll!=='function')return Object.freeze({ok:false,code:'ETP_I18N_DOCUMENT_REQUIRED'});win=win||root;var selector='[data-etp-operational-e3],[data-etp-operational-e4],[data-etp-operational-e5],[data-etp-operational-e6],[data-etp-operational-e7]',nodes=doc.querySelectorAll(selector),controllers=[],getLanguage=function(){return win&&win.SaagarI18n&&typeof win.SaagarI18n.getLanguage==='function'?win.SaagarI18n.getLanguage():'en';};for(var i=0;i<nodes.length;i++){var mounted=attach(nodes[i],{getLanguage:getLanguage});if(mounted.ok)controllers.push(mounted.controller);}function message(event){var data=event&&event.data;if(data&&data.type==='ST_LANG')for(var n=0;n<controllers.length;n++)controllers[n].refresh(data.lang);}if(win&&typeof win.addEventListener==='function')win.addEventListener('message',message);return Object.freeze({ok:true,count:controllers.length,refresh:function(lang){for(var n=0;n<controllers.length;n++)controllers[n].refresh(lang);},destroy:function(){for(var n=0;n<controllers.length;n++)controllers[n].destroy();if(win&&typeof win.removeEventListener==='function')win.removeEventListener('message',message);}});}
  return Object.freeze({VERSION:VERSION,CATALOG_STATUS:STATUS,LANGUAGES:LANGUAGES,phraseCount:PHRASES.length,translate:translate,apply:apply,attach:attach,autoBoot:autoBoot});
});

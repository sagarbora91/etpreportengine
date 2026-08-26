(function(root){
  'use strict';

  var GRID_REASONS=Object.freeze({
    rotation:'seven operational fields require cross-column comparison',
    'turn-audit':'seven audit fields require cross-column comparison',
    preclaims:'five pre-claim fields require cross-column comparison',
    'preclaim-history':'four outcome fields require cross-column comparison',
    followups:'seven follow-up fields require cross-column comparison',
    'followup-history':'five follow-up history fields require cross-column comparison',
    'cro-performance':'ten performance fields require cross-column comparison',
    'lost-value':'four lost-value fields require cross-column comparison',
    'recovered-value':'four recovered-value fields require cross-column comparison',
    'cro-master':'four staff-master fields require cross-column comparison'
  });
  var TABLES=Object.freeze({
    'Order|CRO|Status|Now|Served|Skips|Actions':Object.freeze({workflow:'rotation',strategy:'grid'}),
    'Time|Queue|Expected CRO|Actual CRO|Reason|Next Opp.|By':Object.freeze({workflow:'turn-audit',strategy:'grid'}),
    'CRO|Customer|Mobile|By|':Object.freeze({workflow:'preclaims',strategy:'cards'}),
    'CRO|Customer|Mobile|Outcome':Object.freeze({workflow:'preclaim-history',strategy:'cards'}),
    'Priority|Customer|Owner|Expected|Last contact|Mode|Actions':Object.freeze({workflow:'followups',strategy:'grid'}),
    'Due|Customer|CRO|Mode|Status':Object.freeze({workflow:'followup-history',strategy:'cards'}),
    'CRO|Turns|Assigned|Purchases|Conversion|Sales|Skipped|Pre-claims ✓|Pre-claims ✗|Now':Object.freeze({workflow:'cro-performance',strategy:'grid'}),
    'Customer|Mobile|Lost ₹|Reason':Object.freeze({workflow:'lost-value',strategy:'cards'}),
    'Customer|Mobile|Recovered ₹|Bill':Object.freeze({workflow:'recovered-value',strategy:'cards'}),
    'Name|Code|Status|Actions':Object.freeze({workflow:'cro-master',strategy:'cards'})
  });

  function text(node){return String(node&&node.textContent||'').replace(/\s+/g,' ').trim();}
  function signature(table){
    var cells=table.querySelectorAll('thead th'),parts=[];
    for(var i=0;i<cells.length;i++)parts.push(text(cells[i]));
    return parts.join('|');
  }
  function labelRows(table){
    var heads=table.querySelectorAll('thead th'),rows=table.querySelectorAll('tbody tr');
    for(var r=0;r<rows.length;r++){
      var cells=rows[r].children;
      for(var c=0;c<cells.length&&c<heads.length;c++)if(!cells[c].getAttribute('data-label'))cells[c].setAttribute('data-label',text(heads[c]));
    }
  }
  function decorate(scope){
    if(!scope||typeof scope.querySelectorAll!=='function')return Object.freeze({tables:0,priorityRegions:0});
    var tables=scope.querySelectorAll('table.tbl'),count=0;
    for(var i=0;i<tables.length;i++){
      var table=tables[i],model=TABLES[signature(table)];
      if(!model)continue;
      table.setAttribute('data-saagar-table-workflow','qms-'+model.workflow);
      table.setAttribute('data-saagar-table-strategy',model.strategy);
      if(model.strategy==='grid')table.setAttribute('data-saagar-grid-reason',GRID_REASONS[model.workflow]);
      labelRows(table);
      if(root.SaagarTableFoundation&&typeof root.SaagarTableFoundation.applyStrategy==='function')root.SaagarTableFoundation.applyStrategy(table,model.strategy,model.strategy==='grid'?{reason:GRID_REASONS[model.workflow]}:undefined);
      count++;
    }
    var queues=scope.querySelectorAll('.grid-auto,.grid-3');
    for(var q=0;q<queues.length;q++)if(queues[q].querySelector('.q-card')){
      queues[q].setAttribute('data-saagar-rendered-workflow','qms-live-queue');
      queues[q].setAttribute('data-saagar-rendered-strategy','priority');
    }
    return Object.freeze({tables:count,priorityRegions:scope.querySelectorAll('[data-saagar-rendered-workflow="qms-live-queue"]').length});
  }
  function createPolicy(components){
    if(!components||typeof components.createPolicy!=='function')throw new Error('frozen SaagarRenderedComponents is required');
    var workflows={};
    Object.keys(GRID_REASONS).forEach(function(name){workflows['qms-'+name]={strategy:name==='rotation'||name==='turn-audit'||name==='followups'||name==='cro-performance'?'grid':'cards',reason:GRID_REASONS[name]};});
    workflows['qms-preclaims']={strategy:'cards'};
    workflows['qms-preclaim-history']={strategy:'cards'};
    return components.createPolicy({},workflows);
  }
  var api=Object.freeze({version:1,tableModels:TABLES,gridReasons:GRID_REASONS,decorate:decorate,createPolicy:createPolicy});
  Object.defineProperty(root,'SaagarQmsView',{value:api,enumerable:true,writable:false,configurable:false});
})(typeof window!=='undefined'?window:globalThis);

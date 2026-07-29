import{d as E,h as r,P as le,f as x,Q as ne,W as ce,R as de,S as ue,U as ve,V as U,X as K,c as b,a as O,u as ye,b as ge,e as be,Y as ke,g as se,Z as xe,$ as J,x as re,i as j,j as D,p as A,q as _,w as S,I as ee,N as $e,r as te,H as we,a0 as Se,o as v,_ as he,l as Ce,K as ze,L as Pe,n as a,m as P,J as _e,B as Be,s as F,a1 as Re,F as Ne,G as De,z as Ie}from"./index-CysYKSXN.js";import{g as Ae}from"./metrics-BeqKpLvv.js";import{l as Me}from"./alerts-BGgRatXP.js";import{l as Te}from"./services-B1SWPEMi.js";import{l as We}from"./agents-D0e1_NXl.js";import{l as Oe}from"./disks-CVPMuD-k.js";import{_ as Le,E as X,N as Z}from"./EmptyState-C1Lb0zfz.js";import{f as L,a as ae,b as ie,c as je,d as qe,s as Ge,e as Ve,g as Ue}from"./format-CJiL7b0c.js";import{S as Fe}from"./ServerOutline-BsyU4TXt.js";import{S as Xe,W as Ee}from"./WifiOutline-Cikbp2zz.js";import"./get-DF5tumSm.js";import"./Dropdown-BEsKVdcq.js";import"./Input-m-nh5AJd.js";const Ye={success:r(ue,null),error:r(de,null),warning:r(ce,null),info:r(ne,null)},He=E({name:"ProgressCircle",props:{clsPrefix:{type:String,required:!0},status:{type:String,required:!0},strokeWidth:{type:Number,required:!0},fillColor:[String,Object],railColor:String,railStyle:[String,Object],percentage:{type:Number,default:0},offsetDegree:{type:Number,default:0},showIndicator:{type:Boolean,required:!0},indicatorTextColor:String,unit:String,viewBoxWidth:{type:Number,required:!0},gapDegree:{type:Number,required:!0},gapOffsetDegree:{type:Number,default:0}},setup(e,{slots:l}){const g=x(()=>{const n="gradient",{fillColor:o}=e;return typeof o=="object"?`${n}-${ve(JSON.stringify(o))}`:n});function t(n,o,d,p){const{gapDegree:y,viewBoxWidth:$,strokeWidth:w}=e,u=50,k=0,h=u,c=0,I=2*u,B=50+w/2,C=`M ${B},${B} m ${k},${h}
      a ${u},${u} 0 1 1 ${c},${-I}
      a ${u},${u} 0 1 1 ${-c},${I}`,M=Math.PI*2*u,T={stroke:p==="rail"?d:typeof e.fillColor=="object"?`url(#${g.value})`:d,strokeDasharray:`${Math.min(n,100)/100*(M-y)}px ${$*8}px`,strokeDashoffset:`-${y/2}px`,transformOrigin:o?"center":void 0,transform:o?`rotate(${o}deg)`:void 0};return{pathString:C,pathStyle:T}}const i=()=>{const n=typeof e.fillColor=="object",o=n?e.fillColor.stops[0]:"",d=n?e.fillColor.stops[1]:"";return n&&r("defs",null,r("linearGradient",{id:g.value,x1:"0%",y1:"100%",x2:"100%",y2:"0%"},r("stop",{offset:"0%","stop-color":o}),r("stop",{offset:"100%","stop-color":d})))};return()=>{const{fillColor:n,railColor:o,strokeWidth:d,offsetDegree:p,status:y,percentage:$,showIndicator:w,indicatorTextColor:u,unit:k,gapOffsetDegree:h,clsPrefix:c}=e,{pathString:I,pathStyle:B}=t(100,0,o,"rail"),{pathString:C,pathStyle:M}=t($,p,n,"fill"),T=100+d;return r("div",{class:`${c}-progress-content`,role:"none"},r("div",{class:`${c}-progress-graph`,"aria-hidden":!0},r("div",{class:`${c}-progress-graph-circle`,style:{transform:h?`rotate(${h}deg)`:void 0}},r("svg",{viewBox:`0 0 ${T} ${T}`},i(),r("g",null,r("path",{class:`${c}-progress-graph-circle-rail`,d:I,"stroke-width":d,"stroke-linecap":"round",fill:"none",style:B})),r("g",null,r("path",{class:[`${c}-progress-graph-circle-fill`,$===0&&`${c}-progress-graph-circle-fill--empty`],d:C,"stroke-width":d,"stroke-linecap":"round",fill:"none",style:M}))))),w?r("div",null,l.default?r("div",{class:`${c}-progress-custom-content`,role:"none"},l.default()):y!=="default"?r("div",{class:`${c}-progress-icon`,"aria-hidden":!0},r(le,{clsPrefix:c},{default:()=>Ye[y]})):r("div",{class:`${c}-progress-text`,style:{color:u},role:"none"},r("span",{class:`${c}-progress-text__percentage`},$),r("span",{class:`${c}-progress-text__unit`},k))):null)}}}),Je={success:r(ue,null),error:r(de,null),warning:r(ce,null),info:r(ne,null)},Ke=E({name:"ProgressLine",props:{clsPrefix:{type:String,required:!0},percentage:{type:Number,default:0},railColor:String,railStyle:[String,Object],fillColor:[String,Object],status:{type:String,required:!0},indicatorPlacement:{type:String,required:!0},indicatorTextColor:String,unit:{type:String,default:"%"},processing:{type:Boolean,required:!0},showIndicator:{type:Boolean,required:!0},height:[String,Number],railBorderRadius:[String,Number],fillBorderRadius:[String,Number]},setup(e,{slots:l}){const g=x(()=>U(e.height)),t=x(()=>{var o,d;return typeof e.fillColor=="object"?`linear-gradient(to right, ${(o=e.fillColor)===null||o===void 0?void 0:o.stops[0]} , ${(d=e.fillColor)===null||d===void 0?void 0:d.stops[1]})`:e.fillColor}),i=x(()=>e.railBorderRadius!==void 0?U(e.railBorderRadius):e.height!==void 0?U(e.height,{c:.5}):""),n=x(()=>e.fillBorderRadius!==void 0?U(e.fillBorderRadius):e.railBorderRadius!==void 0?U(e.railBorderRadius):e.height!==void 0?U(e.height,{c:.5}):"");return()=>{const{indicatorPlacement:o,railColor:d,railStyle:p,percentage:y,unit:$,indicatorTextColor:w,status:u,showIndicator:k,processing:h,clsPrefix:c}=e;return r("div",{class:`${c}-progress-content`,role:"none"},r("div",{class:`${c}-progress-graph`,"aria-hidden":!0},r("div",{class:[`${c}-progress-graph-line`,{[`${c}-progress-graph-line--indicator-${o}`]:!0}]},r("div",{class:`${c}-progress-graph-line-rail`,style:[{backgroundColor:d,height:g.value,borderRadius:i.value},p]},r("div",{class:[`${c}-progress-graph-line-fill`,h&&`${c}-progress-graph-line-fill--processing`],style:{maxWidth:`${e.percentage}%`,background:t.value,height:g.value,lineHeight:g.value,borderRadius:n.value}},o==="inside"?r("div",{class:`${c}-progress-graph-line-indicator`,style:{color:w}},l.default?l.default():`${y}${$}`):null)))),k&&o==="outside"?r("div",null,l.default?r("div",{class:`${c}-progress-custom-content`,style:{color:w},role:"none"},l.default()):u==="default"?r("div",{role:"none",class:`${c}-progress-icon ${c}-progress-icon--as-text`,style:{color:w}},y,$):r("div",{class:`${c}-progress-icon`,"aria-hidden":!0},r(le,{clsPrefix:c},{default:()=>Je[u]}))):null)}}});function oe(e,l,g=100){return`m ${g/2} ${g/2-e} a ${e} ${e} 0 1 1 0 ${2*e} a ${e} ${e} 0 1 1 0 -${2*e}`}const Ze=E({name:"ProgressMultipleCircle",props:{clsPrefix:{type:String,required:!0},viewBoxWidth:{type:Number,required:!0},percentage:{type:Array,default:[0]},strokeWidth:{type:Number,required:!0},circleGap:{type:Number,required:!0},showIndicator:{type:Boolean,required:!0},fillColor:{type:Array,default:()=>[]},railColor:{type:Array,default:()=>[]},railStyle:{type:Array,default:()=>[]}},setup(e,{slots:l}){const g=x(()=>e.percentage.map((n,o)=>`${Math.PI*n/100*(e.viewBoxWidth/2-e.strokeWidth/2*(1+2*o)-e.circleGap*o)*2}, ${e.viewBoxWidth*8}`)),t=(i,n)=>{const o=e.fillColor[n],d=typeof o=="object"?o.stops[0]:"",p=typeof o=="object"?o.stops[1]:"";return typeof e.fillColor[n]=="object"&&r("linearGradient",{id:`gradient-${n}`,x1:"100%",y1:"0%",x2:"0%",y2:"100%"},r("stop",{offset:"0%","stop-color":d}),r("stop",{offset:"100%","stop-color":p}))};return()=>{const{viewBoxWidth:i,strokeWidth:n,circleGap:o,showIndicator:d,fillColor:p,railColor:y,railStyle:$,percentage:w,clsPrefix:u}=e;return r("div",{class:`${u}-progress-content`,role:"none"},r("div",{class:`${u}-progress-graph`,"aria-hidden":!0},r("div",{class:`${u}-progress-graph-circle`},r("svg",{viewBox:`0 0 ${i} ${i}`},r("defs",null,w.map((k,h)=>t(k,h))),w.map((k,h)=>r("g",{key:h},r("path",{class:`${u}-progress-graph-circle-rail`,d:oe(i/2-n/2*(1+2*h)-o*h,n,i),"stroke-width":n,"stroke-linecap":"round",fill:"none",style:[{strokeDashoffset:0,stroke:y[h]},$[h]]}),r("path",{class:[`${u}-progress-graph-circle-fill`,k===0&&`${u}-progress-graph-circle-fill--empty`],d:oe(i/2-n/2*(1+2*h)-o*h,n,i),"stroke-width":n,"stroke-linecap":"round",fill:"none",style:{strokeDasharray:g.value[h],strokeDashoffset:0,stroke:typeof p[h]=="object"?`url(#gradient-${h})`:p[h]}})))))),d&&l.default?r("div",null,r("div",{class:`${u}-progress-text`},l.default())):null)}}}),Qe=K([b("progress",{display:"inline-block"},[b("progress-icon",`
 color: var(--n-icon-color);
 transition: color .3s var(--n-bezier);
 `),O("line",`
 width: 100%;
 display: block;
 `,[b("progress-content",`
 display: flex;
 align-items: center;
 `,[b("progress-graph",{flex:1})]),b("progress-custom-content",{marginLeft:"14px"}),b("progress-icon",`
 width: 30px;
 padding-left: 14px;
 height: var(--n-icon-size-line);
 line-height: var(--n-icon-size-line);
 font-size: var(--n-icon-size-line);
 `,[O("as-text",`
 color: var(--n-text-color-line-outer);
 text-align: center;
 width: 40px;
 font-size: var(--n-font-size);
 padding-left: 4px;
 transition: color .3s var(--n-bezier);
 `)])]),O("circle, dashboard",{width:"120px"},[b("progress-custom-content",`
 position: absolute;
 left: 50%;
 top: 50%;
 transform: translateX(-50%) translateY(-50%);
 display: flex;
 align-items: center;
 justify-content: center;
 `),b("progress-text",`
 position: absolute;
 left: 50%;
 top: 50%;
 transform: translateX(-50%) translateY(-50%);
 display: flex;
 align-items: center;
 color: inherit;
 font-size: var(--n-font-size-circle);
 color: var(--n-text-color-circle);
 font-weight: var(--n-font-weight-circle);
 transition: color .3s var(--n-bezier);
 white-space: nowrap;
 `),b("progress-icon",`
 position: absolute;
 left: 50%;
 top: 50%;
 transform: translateX(-50%) translateY(-50%);
 display: flex;
 align-items: center;
 color: var(--n-icon-color);
 font-size: var(--n-icon-size-circle);
 `)]),O("multiple-circle",`
 width: 200px;
 color: inherit;
 `,[b("progress-text",`
 font-weight: var(--n-font-weight-circle);
 color: var(--n-text-color-circle);
 position: absolute;
 left: 50%;
 top: 50%;
 transform: translateX(-50%) translateY(-50%);
 display: flex;
 align-items: center;
 justify-content: center;
 transition: color .3s var(--n-bezier);
 `)]),b("progress-content",{position:"relative"}),b("progress-graph",{position:"relative"},[b("progress-graph-circle",[K("svg",{verticalAlign:"bottom"}),b("progress-graph-circle-fill",`
 stroke: var(--n-fill-color);
 transition:
 opacity .3s var(--n-bezier),
 stroke .3s var(--n-bezier),
 stroke-dasharray .3s var(--n-bezier);
 `,[O("empty",{opacity:0})]),b("progress-graph-circle-rail",`
 transition: stroke .3s var(--n-bezier);
 overflow: hidden;
 stroke: var(--n-rail-color);
 `)]),b("progress-graph-line",[O("indicator-inside",[b("progress-graph-line-rail",`
 height: 16px;
 line-height: 16px;
 border-radius: 10px;
 `,[b("progress-graph-line-fill",`
 height: inherit;
 border-radius: 10px;
 `),b("progress-graph-line-indicator",`
 background: #0000;
 white-space: nowrap;
 text-align: right;
 margin-left: 14px;
 margin-right: 14px;
 height: inherit;
 font-size: 12px;
 color: var(--n-text-color-line-inner);
 transition: color .3s var(--n-bezier);
 `)])]),O("indicator-inside-label",`
 height: 16px;
 display: flex;
 align-items: center;
 `,[b("progress-graph-line-rail",`
 flex: 1;
 transition: background-color .3s var(--n-bezier);
 `),b("progress-graph-line-indicator",`
 background: var(--n-fill-color);
 font-size: 12px;
 transform: translateZ(0);
 display: flex;
 vertical-align: middle;
 height: 16px;
 line-height: 16px;
 padding: 0 10px;
 border-radius: 10px;
 position: absolute;
 white-space: nowrap;
 color: var(--n-text-color-line-inner);
 transition:
 right .2s var(--n-bezier),
 color .3s var(--n-bezier),
 background-color .3s var(--n-bezier);
 `)]),b("progress-graph-line-rail",`
 position: relative;
 overflow: hidden;
 height: var(--n-rail-height);
 border-radius: 5px;
 background-color: var(--n-rail-color);
 transition: background-color .3s var(--n-bezier);
 `,[b("progress-graph-line-fill",`
 background: var(--n-fill-color);
 position: relative;
 border-radius: 5px;
 height: inherit;
 width: 100%;
 max-width: 0%;
 transition:
 background-color .3s var(--n-bezier),
 max-width .2s var(--n-bezier);
 `,[O("processing",[K("&::after",`
 content: "";
 background-image: var(--n-line-bg-processing);
 animation: progress-processing-animation 2s var(--n-bezier) infinite;
 `)])])])])])]),K("@keyframes progress-processing-animation",`
 0% {
 position: absolute;
 left: 0;
 top: 0;
 bottom: 0;
 right: 100%;
 opacity: 1;
 }
 66% {
 position: absolute;
 left: 0;
 top: 0;
 bottom: 0;
 right: 0;
 opacity: 0;
 }
 100% {
 position: absolute;
 left: 0;
 top: 0;
 bottom: 0;
 right: 0;
 opacity: 0;
 }
 `)]),et=Object.assign(Object.assign({},ge.props),{processing:Boolean,type:{type:String,default:"line"},gapDegree:Number,gapOffsetDegree:Number,status:{type:String,default:"default"},railColor:[String,Array],railStyle:[String,Array],color:[String,Array,Object],viewBoxWidth:{type:Number,default:100},strokeWidth:{type:Number,default:7},percentage:[Number,Array],unit:{type:String,default:"%"},showIndicator:{type:Boolean,default:!0},indicatorPosition:{type:String,default:"outside"},indicatorPlacement:{type:String,default:"outside"},indicatorTextColor:String,circleGap:{type:Number,default:1},height:Number,borderRadius:[String,Number],fillBorderRadius:[String,Number],offsetDegree:Number}),tt=E({name:"Progress",props:et,setup(e){const l=x(()=>e.indicatorPlacement||e.indicatorPosition),g=x(()=>{if(e.gapDegree||e.gapDegree===0)return e.gapDegree;if(e.type==="dashboard")return 75}),{mergedClsPrefixRef:t,inlineThemeDisabled:i}=ye(e),n=ge("Progress","-progress",Qe,ke,e,t),o=x(()=>{const{status:p}=e,{common:{cubicBezierEaseInOut:y},self:{fontSize:$,fontSizeCircle:w,railColor:u,railHeight:k,iconSizeCircle:h,iconSizeLine:c,textColorCircle:I,textColorLineInner:B,textColorLineOuter:C,lineBgProcessing:M,fontWeightCircle:T,[se("iconColor",p)]:Y,[se("fillColor",p)]:q}}=n.value;return{"--n-bezier":y,"--n-fill-color":q,"--n-font-size":$,"--n-font-size-circle":w,"--n-font-weight-circle":T,"--n-icon-color":Y,"--n-icon-size-circle":h,"--n-icon-size-line":c,"--n-line-bg-processing":M,"--n-rail-color":u,"--n-rail-height":k,"--n-text-color-circle":I,"--n-text-color-line-inner":B,"--n-text-color-line-outer":C}}),d=i?be("progress",x(()=>e.status[0]),o,e):void 0;return{mergedClsPrefix:t,mergedIndicatorPlacement:l,gapDeg:g,cssVars:i?void 0:o,themeClass:d==null?void 0:d.themeClass,onRender:d==null?void 0:d.onRender}},render(){const{type:e,cssVars:l,indicatorTextColor:g,showIndicator:t,status:i,railColor:n,railStyle:o,color:d,percentage:p,viewBoxWidth:y,strokeWidth:$,mergedIndicatorPlacement:w,unit:u,borderRadius:k,fillBorderRadius:h,height:c,processing:I,circleGap:B,mergedClsPrefix:C,gapDeg:M,gapOffsetDegree:T,themeClass:Y,$slots:q,onRender:s}=this;return s==null||s(),r("div",{class:[Y,`${C}-progress`,`${C}-progress--${e}`,`${C}-progress--${i}`],style:l,"aria-valuemax":100,"aria-valuemin":0,"aria-valuenow":p,role:e==="circle"||e==="line"||e==="dashboard"?"progressbar":"none"},e==="circle"||e==="dashboard"?r(He,{clsPrefix:C,status:i,showIndicator:t,indicatorTextColor:g,railColor:n,fillColor:d,railStyle:o,offsetDegree:this.offsetDegree,percentage:p,viewBoxWidth:y,strokeWidth:$,gapDegree:M===void 0?e==="dashboard"?75:0:M,gapOffsetDegree:T,unit:u},q):e==="line"?r(Ke,{clsPrefix:C,status:i,showIndicator:t,indicatorTextColor:g,railColor:n,fillColor:d,railStyle:o,percentage:p,processing:I,indicatorPlacement:w,unit:u,fillBorderRadius:h,railBorderRadius:k,height:c},q):e==="multiple-circle"?r(Ze,{clsPrefix:C,strokeWidth:$,railColor:n,fillColor:d,railStyle:o,viewBoxWidth:y,percentage:p,showIndicator:t,circleGap:B},q):null)}}),rt=5e3,st=xe("dashboard",()=>{const e=J(null),l=J([]),g=J([]),t=J([]),i=J([]),n=re(!1),o=re(null),d=re(null);let p=null;async function y(u){n.value=!0,o.value=null;try{const k=await Promise.allSettled([Ae(u),Me(u),Te(u),We(u),Oe(u)]),[h,c,I,B,C]=k;h.status==="fulfilled"&&(e.value=h.value),c.status==="fulfilled"&&(l.value=c.value),I.status==="fulfilled"&&(g.value=I.value),B.status==="fulfilled"&&(t.value=B.value),C.status==="fulfilled"&&(i.value=C.value),d.value=new Date}catch(k){o.value=k instanceof Error?k.message:"获取仪表盘数据失败"}finally{n.value=!1}}function $(){w(),y(),p=setInterval(()=>y(),rt)}function w(){p&&(clearInterval(p),p=null)}return{systemMetrics:e,activeAlerts:l,services:g,agents:t,disks:i,loading:n,error:o,lastUpdated:d,fetchAll:y,startPolling:$,stopPolling:w}}),at={class:"zs-stat-card"},it={class:"zs-stat-card-inner"},ot={class:"zs-stat-header"},lt={class:"zs-stat-label"},nt={class:"zs-stat-body"},ct={key:0,class:"zs-stat-unit"},dt={key:0,class:"zs-stat-subtitle"},ut=E({__name:"StatCard",props:{label:{},value:{},unit:{},subtitle:{},icon:{},color:{}},setup(e){return Se(l=>({v403140e5:l.color??"var(--zs-primary)"})),(l,g)=>{const t=$e;return v(),j("div",at,[D("div",it,[D("div",ot,[D("span",lt,A(e.label),1),e.icon?(v(),_(t,{key:0,size:"20",color:e.color??"var(--zs-text-tertiary)"},{default:S(()=>[(v(),_(ee(e.icon)))]),_:1},8,["color"])):te("",!0)]),D("div",nt,[D("span",{class:"zs-stat-value",style:we(e.color?{color:e.color}:{})},A(e.value),5),e.unit?(v(),j("span",ct,A(e.unit),1)):te("",!0)]),e.subtitle?(v(),j("div",dt,A(e.subtitle),1)):te("",!0)])])}}}),Q=he(ut,[["__scopeId","data-v-39700032"]]),gt={class:"zs-dashboard"},ht={class:"zs-gauges-row"},ft={class:"zs-stats-row"},pt={class:"zs-dashboard-grid"},mt={class:"zs-dashboard-col"},vt={key:0,class:"network-list"},yt={class:"network-iface"},bt={key:0,class:"network-speed"},kt={class:"network-rates"},xt={class:"rate-down"},$t={class:"rate-up"},wt={class:"zs-dashboard-col"},St=E({__name:"DashboardView",setup(e){const l=st(),g=Ie(),{t}=Ce();ze(()=>l.startPolling()),Pe(()=>l.stopPolling());const i=x(()=>l.systemMetrics),n=x(()=>i.value?Math.round(i.value.cpu.usagePercent):0),o=x(()=>i.value?i.value.memory.usedBytes:0),d=x(()=>i.value?i.value.memory.totalBytes:1),p=x(()=>Math.round(o.value/d.value*100)),y=x(()=>{var s,f;return(f=(s=i.value)==null?void 0:s.fileSystems)!=null&&f.length?i.value.fileSystems.reduce((m,z)=>m+(z.totalBytes??0),0):0}),$=x(()=>{var s,f;return(f=(s=i.value)==null?void 0:s.fileSystems)!=null&&f.length?i.value.fileSystems.reduce((m,z)=>m+(z.usedBytes??0),0):0}),w=x(()=>y.value?Math.round($.value/y.value*100):0),u=x(()=>l.activeAlerts.filter(s=>s.severity.toLowerCase()==="critical"||s.severity.toLowerCase()==="error").length),k=x(()=>{const s=l.disks;if(!s.length)return 100;const f=s.filter(m=>{var z,N;return((z=m.smartStatus)==null?void 0:z.toLowerCase())==="ok"||((N=m.smartStatus)==null?void 0:N.toLowerCase())==="passed"}).length;return Math.round(f/s.length*100)}),h=x(()=>l.services.filter(s=>s.status==="Running").length);function c(s){return s>=90?"#ef4444":s>=70?"#f59e0b":s>=50?"#4a90d9":"#34c759"}function I({cx:s,cy:f,r:m,stroke:z,pct:N}){const H=c(N);if(N<=0)return{d:"",color:H};const V=-Math.PI/2,R=V+N/100*2*Math.PI,G=s+m*Math.cos(V),W=f+m*Math.sin(V),fe=s+m*Math.cos(R),pe=f+m*Math.sin(R),me=N>50?1:0;return{d:`M ${G} ${W} A ${m} ${m} 0 ${me} 1 ${fe} ${pe}`,color:H}}function B(s,f,m){const z=I({cx:44,cy:44,r:34,stroke:"",pct:s});return r("div",{class:"zs-gauge-item",onClick:f==="CPU"?()=>g.push({name:"Services"}):f==="RAM"?()=>g.push({name:"Services"}):()=>g.push({name:"Storage"})},[r("div",{class:"zs-gauge"},[r("svg",{width:88,height:88,viewBox:"0 0 88 88"},[r("circle",{cx:44,cy:44,r:34,fill:"none",stroke:"var(--zs-border)","stroke-width":8}),z.d?r("path",{d:z.d,fill:"none",stroke:z.color,"stroke-width":8,"stroke-linecap":"round"}):null]),r("div",{class:"zs-gauge-center"},[r("span",{class:"zs-gauge-pct"},`${s}%`),r("span",{class:"zs-gauge-label"},f)])]),r("div",{class:"zs-gauge-detail"},m)])}const C=[{title:()=>t("dashboard.devicePath"),key:"path",ellipsis:{tooltip:!0},width:160},{title:()=>t("dashboard.model"),key:"model",ellipsis:{tooltip:!0}},{title:()=>t("dashboard.capacity"),key:"sizeBytes",render:s=>L(s.sizeBytes)},{title:()=>t("dashboard.smartStatus")??"SMART",key:"smartStatus",width:90,render:s=>{var f,m;return r(Z,{type:((f=s.smartStatus)==null?void 0:f.toLowerCase())==="ok"||((m=s.smartStatus)==null?void 0:m.toLowerCase())==="passed"?"success":"warning",size:"small"},{default:()=>s.smartStatus??t("common.unknown")})}},{title:()=>t("dashboard.temperature"),key:"temperatureCelsius",width:70,render:s=>je(s.temperatureCelsius)},{title:()=>t("dashboard.usage"),key:"usedPercent",width:80,render:s=>qe(s.usedPercent)}],M=[{title:"ID",key:"serviceId",ellipsis:{tooltip:!0},width:140},{title:()=>t("common.status"),key:"status",width:90,render:s=>r(Z,{type:Ge(s.status),size:"small"},{default:()=>s.status})},{title:()=>t("common.type"),key:"type",width:90},{title:()=>t("services.cpu"),key:"cpuPercent",width:70,render:s=>`${s.cpuPercent.toFixed(1)}%`},{title:()=>t("services.memory"),key:"memoryBytes",width:90,render:s=>L(s.memoryBytes)},{title:()=>t("services.uptime"),key:"uptime",width:100,render:s=>ae(s.uptime)}],T=[{title:"ID",key:"serviceId",ellipsis:{tooltip:!0},width:140},{title:()=>t("common.name"),key:"displayName",ellipsis:{tooltip:!0}},{title:()=>t("common.type"),key:"type",width:80}],Y=[{title:()=>t("dashboard.mountPoint"),key:"mountPoint",ellipsis:{tooltip:!0}},{title:()=>t("dashboard.device"),key:"device",ellipsis:{tooltip:!0},width:140},{title:()=>t("dashboard.filesystemType"),key:"fileSystemType",width:80},{title:()=>t("dashboard.capacity"),key:"totalBytes",width:100,render:s=>L(s.totalBytes)},{title:()=>t("dashboard.used"),key:"usedBytes",width:100,render:s=>L(s.usedBytes)},{title:()=>t("dashboard.usage"),key:"usedPercent",width:80,render:s=>r(tt,{type:s.usedPercent>90?"error":s.usedPercent>75?"warning":"success",percentage:Math.round(s.usedPercent),showIndicator:!1,height:18,borderRadius:"4px"})}],q=[{title:()=>t("alerts.severity"),key:"severity",width:80,render:s=>r(Z,{type:Ve(s.severity),size:"small"},{default:()=>s.severity})},{title:()=>t("alerts.message"),key:"message",ellipsis:{tooltip:!0}},{title:()=>t("alerts.triggeredAt"),key:"triggeredAt",width:170,render:s=>Ue(s.triggeredAt)}];return(s,f)=>{var H,V;const m=Be,z=Le,N=Re;return v(),j("div",gt,[D("div",ht,[(v(),_(ee(B(n.value,"CPU",i.value?`${i.value.cpu.logicalProcessorCount} ${a(t)("dashboard.logicalCores")} · ${n.value}%`:"—")))),(v(),_(ee(B(p.value,"RAM",`${a(L)(o.value)} / ${a(L)(d.value)}`)))),(v(),_(ee(B(w.value,a(t)("nav.storage"),`${a(L)($.value)} / ${a(L)(y.value)}`))))]),D("div",ft,[P(Q,{label:a(t)("dashboard.hostUptime"),value:i.value?a(ae)(i.value.host.uptime):"—",icon:a(Fe),subtitle:i.value?`Load ${i.value.host.loadAverage1.toFixed(1)} / ${i.value.host.loadAverage5.toFixed(1)} / ${i.value.host.loadAverage15.toFixed(1)}`:void 0,color:"#4a90d9"},null,8,["label","value","icon","subtitle"]),P(Q,{label:a(t)("dashboard.diskHealth"),value:`${k.value}%`,icon:a(Xe),color:k.value===100?"#34c759":k.value>=80?"#f59e0b":"#ef4444",subtitle:`${a(t)("dashboard.disksCount",{count:a(l).disks.length})} · ${a(t)("dashboard.servicesRunning",{count:h.value})}`},null,8,["label","value","icon","color","subtitle"]),P(Q,{label:a(t)("dashboard.activeAlerts"),value:a(l).activeAlerts.length,icon:a(_e),color:u.value>0?"#ef4444":a(l).activeAlerts.length>0?"#f59e0b":"#34c759",subtitle:`${u.value} ${a(t)("dashboard.criticalAlerts")}`},null,8,["label","value","icon","color","subtitle"]),P(Q,{label:a(t)("dashboard.networkTraffic"),value:(V=(H=i.value)==null?void 0:H.networks)!=null&&V.length?`${i.value.networks.filter(R=>R.isUp).length}/${i.value.networks.length}`:"—",icon:a(Ee),color:"#0ea5e9",subtitle:a(t)("dashboard.networkInterfaces")},null,8,["label","value","icon","subtitle"])]),D("div",pt,[D("div",mt,[P(N,{title:a(t)("dashboard.disks"),size:"small",bordered:!0,class:"zs-dashboard-card"},{"header-extra":S(()=>[P(m,{text:"",size:"small",onClick:f[0]||(f[0]=R=>a(g).push({name:"Storage"}))},{default:S(()=>[F(A(a(t)("dashboard.viewDetails")),1)]),_:1})]),default:S(()=>[a(l).disks.length?(v(),_(z,{key:0,columns:C,data:a(l).disks,bordered:!1,size:"small","max-height":280,striped:""},null,8,["data"])):(v(),_(X,{key:1,message:a(t)("dashboard.noDisks")},null,8,["message"]))]),_:1},8,["title"]),P(N,{title:a(t)("dashboard.filesystems"),size:"small",bordered:!0,class:"zs-dashboard-card",style:{"margin-top":"16px"}},{default:S(()=>{var R,G;return[(G=(R=i.value)==null?void 0:R.fileSystems)!=null&&G.length?(v(),_(z,{key:0,columns:Y,data:i.value.fileSystems,bordered:!1,size:"small","max-height":220,striped:""},null,8,["data"])):(v(),_(X,{key:1,message:a(t)("dashboard.noFilesystems")},null,8,["message"]))]}),_:1},8,["title"]),P(N,{title:a(t)("dashboard.networkTraffic"),size:"small",bordered:!0,class:"zs-dashboard-card",style:{"margin-top":"16px"}},{"header-extra":S(()=>[P(m,{text:"",size:"small",onClick:f[1]||(f[1]=R=>a(g).push({name:"Network"}))},{default:S(()=>[F(A(a(t)("dashboard.viewDetails")),1)]),_:1})]),default:S(()=>{var R,G;return[(G=(R=i.value)==null?void 0:R.networks)!=null&&G.length?(v(),j("div",vt,[(v(!0),j(Ne,null,De(i.value.networks.slice(0,4),W=>(v(),j("div",{key:W.interface,class:"network-row"},[D("div",yt,[P(a(Z),{type:W.isUp?"success":"default",size:"small",round:""},{default:S(()=>[F(A(W.interface),1)]),_:2},1032,["type"]),W.linkSpeedMbps?(v(),j("span",bt,A(W.linkSpeedMbps)+" Mbps",1)):te("",!0)]),D("div",kt,[D("span",xt,"↓ "+A(a(ie)(W.receiveBytesPerSecond)),1),D("span",$t,"↑ "+A(a(ie)(W.transmitBytesPerSecond)),1)])]))),128))])):(v(),_(X,{key:1,message:a(t)("dashboard.noNetwork")},null,8,["message"]))]}),_:1},8,["title"])]),D("div",wt,[P(N,{title:a(t)("dashboard.servicesStatus"),size:"small",bordered:!0,class:"zs-dashboard-card"},{"header-extra":S(()=>[P(m,{text:"",size:"small",onClick:f[2]||(f[2]=R=>a(g).push({name:"Services"}))},{default:S(()=>[F(A(a(t)("dashboard.viewDetails")),1)]),_:1})]),default:S(()=>[a(l).services.length?(v(),_(z,{key:0,columns:M,data:a(l).services,bordered:!1,size:"small","max-height":280,striped:""},null,8,["data"])):(v(),_(X,{key:1,message:a(t)("dashboard.noServices")},null,8,["message"]))]),_:1},8,["title"]),P(N,{title:a(t)("dashboard.agentContainers"),size:"small",bordered:!0,class:"zs-dashboard-card",style:{"margin-top":"16px"}},{"header-extra":S(()=>[P(m,{text:"",size:"small",onClick:f[3]||(f[3]=R=>a(g).push({name:"Agents"}))},{default:S(()=>[F(A(a(t)("dashboard.viewDetails")),1)]),_:1})]),default:S(()=>[a(l).agents.length?(v(),_(z,{key:0,columns:T,data:a(l).agents,bordered:!1,size:"small","max-height":180,striped:""},null,8,["data"])):(v(),_(X,{key:1,message:a(t)("dashboard.noAgents")},null,8,["message"]))]),_:1},8,["title"]),P(N,{title:a(t)("dashboard.activeAlerts"),size:"small",bordered:!0,class:"zs-dashboard-card",style:{"margin-top":"16px"}},{"header-extra":S(()=>[P(m,{text:"",size:"small",onClick:f[4]||(f[4]=R=>a(g).push({name:"Alerts"}))},{default:S(()=>[F(A(a(t)("dashboard.viewDetails")),1)]),_:1})]),default:S(()=>[a(l).activeAlerts.length?(v(),_(z,{key:0,columns:q,data:a(l).activeAlerts,bordered:!1,size:"small","max-height":260,striped:""},null,8,["data"])):(v(),_(X,{key:1,message:a(t)("dashboard.noAlerts"),description:a(t)("dashboard.allNormal")},null,8,["message","description"]))]),_:1},8,["title"])])])])}}}),Ot=he(St,[["__scopeId","data-v-fd9ef2bb"]]);export{Ot as default};

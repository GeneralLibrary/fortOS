import{a8 as de,f as B,a9 as ce,aa as ue,X as D,c as i,a as x,ab as N,d as W,h as m,u as X,K as se,M as K,b as F,e as Z,x as M,ac as Y,ad as q,ae as he,af as pe,ag as me,ah as ge,ai as be,g as G,F as fe,aj as ve,Z as _e,$ as J,l as ye,i as ee,m as b,w as g,n as d,B as te,s as S,p as $,q as A,a1 as we,j as $e,r as oe,o as E,_ as xe}from"./index-CysYKSXN.js";import{l as Ce,g as Se,r as ae}from"./disks-CVPMuD-k.js";import{P as ze}from"./PageHeader-DccjYFN3.js";import{g as ke,_ as je,E as Pe,N as De}from"./EmptyState-C1Lb0zfz.js";import{f as re,c as U,d as ne}from"./format-CJiL7b0c.js";import{u as Re}from"./get-DF5tumSm.js";import{_ as Ne,a as Be}from"./DrawerContent-BQ7yapMY.js";import{_ as Te}from"./Spin-C26HWWng.js";import{_ as Le}from"./Alert-Dl2AgPz3.js";import"./Dropdown-BEsKVdcq.js";import"./Input-m-nh5AJd.js";function le(o,e="default",t=[]){const{children:r}=o;if(r!==null&&typeof r=="object"&&!Array.isArray(r)){const l=r[e];if(typeof l=="function")return l()}return t}function Ee(o,e){const t=de(ce,null);return B(()=>o.hljs||(t==null?void 0:t.mergedHljsRef.value))}function Ie(o){const{textColor2:e,fontSize:t,fontWeightStrong:r,textColor3:l}=o;return{textColor:e,fontSize:t,fontWeightStrong:r,"mono-3":"#a0a1a7","hue-1":"#0184bb","hue-2":"#4078f2","hue-3":"#a626a4","hue-4":"#50a14f","hue-5":"#e45649","hue-5-2":"#c91243","hue-6":"#986801","hue-6-2":"#c18401",lineNumberTextColor:l}}const Oe={common:ue,self:Ie},He=D([i("code",`
 font-size: var(--n-font-size);
 font-family: var(--n-font-family);
 `,[x("show-line-numbers",`
 display: flex;
 `),N("line-numbers",`
 user-select: none;
 padding-right: 12px;
 text-align: right;
 transition: color .3s var(--n-bezier);
 color: var(--n-line-number-text-color);
 `),x("word-wrap",[D("pre",`
 white-space: pre-wrap;
 word-break: break-all;
 `)]),D("pre",`
 margin: 0;
 line-height: inherit;
 font-size: inherit;
 font-family: inherit;
 `),D("[class^=hljs]",`
 color: var(--n-text-color);
 transition: 
 color .3s var(--n-bezier),
 background-color .3s var(--n-bezier);
 `)]),({props:o})=>{const e=`${o.bPrefix}code`;return[`${e} .hljs-comment,
 ${e} .hljs-quote {
 color: var(--n-mono-3);
 font-style: italic;
 }`,`${e} .hljs-doctag,
 ${e} .hljs-keyword,
 ${e} .hljs-formula {
 color: var(--n-hue-3);
 }`,`${e} .hljs-section,
 ${e} .hljs-name,
 ${e} .hljs-selector-tag,
 ${e} .hljs-deletion,
 ${e} .hljs-subst {
 color: var(--n-hue-5);
 }`,`${e} .hljs-literal {
 color: var(--n-hue-1);
 }`,`${e} .hljs-string,
 ${e} .hljs-regexp,
 ${e} .hljs-addition,
 ${e} .hljs-attribute,
 ${e} .hljs-meta-string {
 color: var(--n-hue-4);
 }`,`${e} .hljs-built_in,
 ${e} .hljs-class .hljs-title {
 color: var(--n-hue-6-2);
 }`,`${e} .hljs-attr,
 ${e} .hljs-variable,
 ${e} .hljs-template-variable,
 ${e} .hljs-type,
 ${e} .hljs-selector-class,
 ${e} .hljs-selector-attr,
 ${e} .hljs-selector-pseudo,
 ${e} .hljs-number {
 color: var(--n-hue-6);
 }`,`${e} .hljs-symbol,
 ${e} .hljs-bullet,
 ${e} .hljs-link,
 ${e} .hljs-meta,
 ${e} .hljs-selector-id,
 ${e} .hljs-title {
 color: var(--n-hue-2);
 }`,`${e} .hljs-emphasis {
 font-style: italic;
 }`,`${e} .hljs-strong {
 font-weight: var(--n-font-weight-strong);
 }`,`${e} .hljs-link {
 text-decoration: underline;
 }`]}]),Ve=Object.assign(Object.assign({},F.props),{language:String,code:{type:String,default:""},trim:{type:Boolean,default:!0},hljs:Object,uri:Boolean,inline:Boolean,wordWrap:Boolean,showLineNumbers:Boolean,internalFontSize:Number,internalNoHighlight:Boolean}),Me=W({name:"Code",props:Ve,setup(o,{slots:e}){const{internalNoHighlight:t}=o,{mergedClsPrefixRef:r,inlineThemeDisabled:l}=X(),c=M(null),h=t?{value:void 0}:Ee(o),_=(s,f,v)=>{const{value:y}=h;return!y||!(s&&y.getLanguage(s))?null:y.highlight(v?f.trim():f,{language:s}).value},p=B(()=>o.inline||o.wordWrap?!1:o.showLineNumbers),n=()=>{if(e.default)return;const{value:s}=c;if(!s)return;const{language:f}=o,v=o.uri?window.decodeURIComponent(o.code):o.code;if(f){const w=_(f,v,o.trim);if(w!==null){if(o.inline)s.innerHTML=w;else{const a=s.querySelector(".__code__");a&&s.removeChild(a);const j=document.createElement("pre");j.className="__code__",j.innerHTML=w,s.appendChild(j)}return}}if(o.inline){s.textContent=v;return}const y=s.querySelector(".__code__");if(y)y.textContent=v;else{const w=document.createElement("pre");w.className="__code__",w.textContent=v,s.innerHTML="",s.appendChild(w)}};se(n),K(Y(o,"language"),n),K(Y(o,"code"),n),t||K(h,n);const z=F("Code","-code",He,Oe,o,r),u=B(()=>{const{common:{cubicBezierEaseInOut:s,fontFamilyMono:f},self:{textColor:v,fontSize:y,fontWeightStrong:w,lineNumberTextColor:a,"mono-3":j,"hue-1":I,"hue-2":P,"hue-3":C,"hue-4":R,"hue-5":O,"hue-5-2":T,"hue-6":H,"hue-6-2":V}}=z.value,{internalFontSize:L}=o;return{"--n-font-size":L?`${L}px`:y,"--n-font-family":f,"--n-font-weight-strong":w,"--n-bezier":s,"--n-text-color":v,"--n-mono-3":j,"--n-hue-1":I,"--n-hue-2":P,"--n-hue-3":C,"--n-hue-4":R,"--n-hue-5":O,"--n-hue-5-2":T,"--n-hue-6":H,"--n-hue-6-2":V,"--n-line-number-text-color":a}}),k=l?Z("code",B(()=>`${o.internalFontSize||"a"}`),u,o):void 0;return{mergedClsPrefix:r,codeRef:c,mergedShowLineNumbers:p,lineNumbers:B(()=>{let s=1;const f=[];let v=!1;for(const y of o.code)y===`
`?(v=!0,f.push(s++)):v=!1;return v||f.push(s++),f.join(`
`)}),cssVars:l?void 0:u,themeClass:k==null?void 0:k.themeClass,onRender:k==null?void 0:k.onRender}},render(){var o,e;const{mergedClsPrefix:t,wordWrap:r,mergedShowLineNumbers:l,onRender:c}=this;return c==null||c(),m("code",{class:[`${t}-code`,this.themeClass,r&&`${t}-code--word-wrap`,l&&`${t}-code--show-line-numbers`],style:this.cssVars,ref:"codeRef"},l?m("pre",{class:`${t}-code__line-numbers`},this.lineNumbers):null,(e=(o=this.$slots).default)===null||e===void 0?void 0:e.call(o))}}),Ae=D([i("descriptions",{fontSize:"var(--n-font-size)"},[i("descriptions-separator",`
 display: inline-block;
 margin: 0 8px 0 2px;
 `),i("descriptions-table-wrapper",[i("descriptions-table",[i("descriptions-table-row",[i("descriptions-table-header",{padding:"var(--n-th-padding)"}),i("descriptions-table-content",{padding:"var(--n-td-padding)"})])])]),q("bordered",[i("descriptions-table-wrapper",[i("descriptions-table",[i("descriptions-table-row",[D("&:last-child",[i("descriptions-table-content",{paddingBottom:0})])])])])]),x("left-label-placement",[i("descriptions-table-content",[D("> *",{verticalAlign:"top"})])]),x("left-label-align",[D("th",{textAlign:"left"})]),x("center-label-align",[D("th",{textAlign:"center"})]),x("right-label-align",[D("th",{textAlign:"right"})]),x("bordered",[i("descriptions-table-wrapper",`
 border-radius: var(--n-border-radius);
 overflow: hidden;
 background: var(--n-merged-td-color);
 border: 1px solid var(--n-merged-border-color);
 `,[i("descriptions-table",[i("descriptions-table-row",[D("&:not(:last-child)",[i("descriptions-table-content",{borderBottom:"1px solid var(--n-merged-border-color)"}),i("descriptions-table-header",{borderBottom:"1px solid var(--n-merged-border-color)"})]),i("descriptions-table-header",`
 font-weight: 400;
 background-clip: padding-box;
 background-color: var(--n-merged-th-color);
 `,[D("&:not(:last-child)",{borderRight:"1px solid var(--n-merged-border-color)"})]),i("descriptions-table-content",[D("&:not(:last-child)",{borderRight:"1px solid var(--n-merged-border-color)"})])])])])]),i("descriptions-header",`
 font-weight: var(--n-th-font-weight);
 font-size: 18px;
 transition: color .3s var(--n-bezier);
 line-height: var(--n-line-height);
 margin-bottom: 16px;
 color: var(--n-title-text-color);
 `),i("descriptions-table-wrapper",`
 transition:
 background-color .3s var(--n-bezier),
 border-color .3s var(--n-bezier);
 `,[i("descriptions-table",`
 width: 100%;
 border-collapse: separate;
 border-spacing: 0;
 box-sizing: border-box;
 `,[i("descriptions-table-row",`
 box-sizing: border-box;
 transition: border-color .3s var(--n-bezier);
 `,[i("descriptions-table-header",`
 font-weight: var(--n-th-font-weight);
 line-height: var(--n-line-height);
 display: table-cell;
 box-sizing: border-box;
 color: var(--n-th-text-color);
 transition:
 color .3s var(--n-bezier),
 background-color .3s var(--n-bezier),
 border-color .3s var(--n-bezier);
 `),i("descriptions-table-content",`
 vertical-align: top;
 line-height: var(--n-line-height);
 display: table-cell;
 box-sizing: border-box;
 color: var(--n-td-text-color);
 transition:
 color .3s var(--n-bezier),
 background-color .3s var(--n-bezier),
 border-color .3s var(--n-bezier);
 `,[N("content",`
 transition: color .3s var(--n-bezier);
 display: inline-block;
 color: var(--n-td-text-color);
 `)]),N("label",`
 font-weight: var(--n-th-font-weight);
 transition: color .3s var(--n-bezier);
 display: inline-block;
 margin-right: 14px;
 color: var(--n-th-text-color);
 `)])])])]),i("descriptions-table-wrapper",`
 --n-merged-th-color: var(--n-th-color);
 --n-merged-td-color: var(--n-td-color);
 --n-merged-border-color: var(--n-border-color);
 `),he(i("descriptions-table-wrapper",`
 --n-merged-th-color: var(--n-th-color-modal);
 --n-merged-td-color: var(--n-td-color-modal);
 --n-merged-border-color: var(--n-border-color-modal);
 `)),pe(i("descriptions-table-wrapper",`
 --n-merged-th-color: var(--n-th-color-popover);
 --n-merged-td-color: var(--n-td-color-popover);
 --n-merged-border-color: var(--n-border-color-popover);
 `))]),ie="DESCRIPTION_ITEM_FLAG";function Fe(o){return typeof o=="object"&&o&&!Array.isArray(o)?o.type&&o.type[ie]:!1}const We=Object.assign(Object.assign({},F.props),{title:String,column:{type:Number,default:3},columns:Number,labelPlacement:{type:String,default:"top"},labelAlign:{type:String,default:"left"},separator:{type:String,default:":"},size:String,bordered:Boolean,labelClass:String,labelStyle:[Object,String],contentClass:String,contentStyle:[Object,String]}),qe=W({name:"Descriptions",props:We,slots:Object,setup(o){const{mergedClsPrefixRef:e,inlineThemeDisabled:t,mergedComponentPropsRef:r}=X(o),l=B(()=>{var p,n;return o.size||((n=(p=r==null?void 0:r.value)===null||p===void 0?void 0:p.Descriptions)===null||n===void 0?void 0:n.size)||"medium"}),c=F("Descriptions","-descriptions",Ae,be,o,e),h=B(()=>{const{bordered:p}=o,n=l.value,{common:{cubicBezierEaseInOut:z},self:{titleTextColor:u,thColor:k,thColorModal:s,thColorPopover:f,thTextColor:v,thFontWeight:y,tdTextColor:w,tdColor:a,tdColorModal:j,tdColorPopover:I,borderColor:P,borderColorModal:C,borderColorPopover:R,borderRadius:O,lineHeight:T,[G("fontSize",n)]:H,[G(p?"thPaddingBordered":"thPadding",n)]:V,[G(p?"tdPaddingBordered":"tdPadding",n)]:L}}=c.value;return{"--n-title-text-color":u,"--n-th-padding":V,"--n-td-padding":L,"--n-font-size":H,"--n-bezier":z,"--n-th-font-weight":y,"--n-line-height":T,"--n-th-text-color":v,"--n-td-text-color":w,"--n-th-color":k,"--n-th-color-modal":s,"--n-th-color-popover":f,"--n-td-color":a,"--n-td-color-modal":j,"--n-td-color-popover":I,"--n-border-radius":O,"--n-border-color":P,"--n-border-color-modal":C,"--n-border-color-popover":R}}),_=t?Z("descriptions",B(()=>{let p="";const{bordered:n}=o;return n&&(p+="a"),p+=l.value[0],p}),h,o):void 0;return{mergedClsPrefix:e,cssVars:t?void 0:h,themeClass:_==null?void 0:_.themeClass,onRender:_==null?void 0:_.onRender,compitableColumn:Re(o,["columns","column"]),inlineThemeDisabled:t,mergedSize:l}},render(){const o=this.$slots.default,e=o?me(o()):[];e.length;const{contentClass:t,labelClass:r,compitableColumn:l,labelPlacement:c,labelAlign:h,mergedSize:_,bordered:p,title:n,cssVars:z,mergedClsPrefix:u,separator:k,onRender:s}=this;s==null||s();const f=e.filter(a=>Fe(a)),v={span:0,row:[],secondRow:[],rows:[]},w=f.reduce((a,j,I)=>{const P=j.props||{},C=f.length-1===I,R=["label"in P?P.label:le(j,"label")],O=[le(j)],T=P.span||1,H=a.span;a.span+=T;const V=P.labelStyle||P["label-style"]||this.labelStyle,L=P.contentStyle||P["content-style"]||this.contentStyle;if(c==="left")p?a.row.push(m("th",{class:[`${u}-descriptions-table-header`,r],colspan:1,style:V},R),m("td",{class:[`${u}-descriptions-table-content`,t],colspan:C?(l-H)*2+1:T*2-1,style:L},O)):a.row.push(m("td",{class:`${u}-descriptions-table-content`,colspan:C?(l-H)*2:T*2},m("span",{class:[`${u}-descriptions-table-content__label`,r],style:V},[...R,k&&m("span",{class:`${u}-descriptions-separator`},k)]),m("span",{class:[`${u}-descriptions-table-content__content`,t],style:L},O)));else{const Q=C?(l-H)*2:T*2;a.row.push(m("th",{class:[`${u}-descriptions-table-header`,r],colspan:Q,style:V},R)),a.secondRow.push(m("td",{class:[`${u}-descriptions-table-content`,t],colspan:Q,style:L},O))}return(a.span>=l||C)&&(a.span=0,a.row.length&&(a.rows.push(a.row),a.row=[]),c!=="left"&&a.secondRow.length&&(a.rows.push(a.secondRow),a.secondRow=[])),a},v).rows.map(a=>m("tr",{class:`${u}-descriptions-table-row`},a));return m("div",{style:z,class:[`${u}-descriptions`,this.themeClass,`${u}-descriptions--${c}-label-placement`,`${u}-descriptions--${h}-label-align`,`${u}-descriptions--${_}-size`,p&&`${u}-descriptions--bordered`]},n||this.$slots.header?m("div",{class:`${u}-descriptions-header`},n||ke(this,"header")):null,m("div",{class:`${u}-descriptions-table-wrapper`},m("table",{class:`${u}-descriptions-table`},m("tbody",null,c==="top"&&m("tr",{class:`${u}-descriptions-table-row`,style:{visibility:"collapse"}},ge(l*2,m("td",null))),w))))}}),Ke={label:String,span:{type:Number,default:1},labelClass:String,labelStyle:[Object,String],contentClass:String,contentStyle:[Object,String]},Ge=W({name:"DescriptionsItem",[ie]:!0,props:Ke,slots:Object,render(){return null}}),Je=i("divider",`
 position: relative;
 display: flex;
 width: 100%;
 box-sizing: border-box;
 font-size: 16px;
 color: var(--n-text-color);
 transition:
 color .3s var(--n-bezier),
 background-color .3s var(--n-bezier);
`,[q("vertical",`
 margin-top: 24px;
 margin-bottom: 24px;
 `,[q("no-title",`
 display: flex;
 align-items: center;
 `)]),N("title",`
 display: flex;
 align-items: center;
 margin-left: 12px;
 margin-right: 12px;
 white-space: nowrap;
 font-weight: var(--n-font-weight);
 `),x("title-position-left",[N("line",[x("left",{width:"28px"})])]),x("title-position-right",[N("line",[x("right",{width:"28px"})])]),x("dashed",[N("line",`
 background-color: #0000;
 height: 0px;
 width: 100%;
 border-style: dashed;
 border-width: 1px 0 0;
 `)]),x("vertical",`
 display: inline-block;
 height: 1em;
 margin: 0 8px;
 vertical-align: middle;
 width: 1px;
 `),N("line",`
 border: none;
 transition: background-color .3s var(--n-bezier), border-color .3s var(--n-bezier);
 height: 1px;
 width: 100%;
 margin: 0;
 `),q("dashed",[N("line",{backgroundColor:"var(--n-color)"})]),x("dashed",[N("line",{borderColor:"var(--n-color)"})]),x("vertical",{backgroundColor:"var(--n-color)"})]),Ue=Object.assign(Object.assign({},F.props),{titlePlacement:{type:String,default:"center"},dashed:Boolean,vertical:Boolean}),Xe=W({name:"Divider",props:Ue,setup(o){const{mergedClsPrefixRef:e,inlineThemeDisabled:t}=X(o),r=F("Divider","-divider",Je,ve,o,e),l=B(()=>{const{common:{cubicBezierEaseInOut:h},self:{color:_,textColor:p,fontWeight:n}}=r.value;return{"--n-bezier":h,"--n-color":_,"--n-text-color":p,"--n-font-weight":n}}),c=t?Z("divider",void 0,l,o):void 0;return{mergedClsPrefix:e,cssVars:t?void 0:l,themeClass:c==null?void 0:c.themeClass,onRender:c==null?void 0:c.onRender}},render(){var o;const{$slots:e,titlePlacement:t,vertical:r,dashed:l,cssVars:c,mergedClsPrefix:h}=this;return(o=this.onRender)===null||o===void 0||o.call(this),m("div",{role:"separator",class:[`${h}-divider`,this.themeClass,{[`${h}-divider--vertical`]:r,[`${h}-divider--no-title`]:!e.default,[`${h}-divider--dashed`]:l,[`${h}-divider--title-position-${t}`]:e.default&&t}],style:c},r?null:m("div",{class:`${h}-divider__line ${h}-divider__line--left`}),!r&&e.default?m(fe,null,m("div",{class:`${h}-divider__title`},this.$slots),m("div",{class:`${h}-divider__line ${h}-divider__line--right`})):null)}}),Ze=_e("disks",()=>{const o=J([]),e=J(null),t=J(null),r=M(!1),l=M(null);async function c(p){r.value=!0,l.value=null;try{o.value=await Ce(p)}catch(n){l.value=n instanceof Error?n.message:"获取磁盘列表失败"}finally{r.value=!1}}async function h(p){r.value=!0,l.value=null;try{e.value=await Se(p)}catch(n){l.value=n instanceof Error?n.message:"获取磁盘详情失败"}finally{r.value=!1}}async function _(p){r.value=!0,l.value=null;try{t.value=await ae(p)}catch(n){l.value=n instanceof Error?n.message:"SMART 检测失败"}finally{r.value=!1}}return{disks:o,selectedDisk:e,smartData:t,loading:r,error:l,fetchDisks:c,fetchDiskDetail:h,checkSmart:_}}),Qe={class:"storage-page"},Ye={style:{margin:"0 0 12px"}},et={key:1},tt=W({__name:"StorageView",setup(o){const e=Ze(),{t}=ye(),r=M(null),l=M(null),c=M(!1),h=M(!1);se(()=>e.fetchDisks());async function _(n){r.value=n,h.value=!0,c.value=!0;try{l.value=await ae(n.path)}catch{l.value=null}finally{c.value=!1}}const p=[{title:()=>t("storage.devicePath"),key:"path",ellipsis:{tooltip:!0},width:140},{title:()=>t("storage.model"),key:"model",ellipsis:{tooltip:!0}},{title:()=>t("storage.serial"),key:"serial",ellipsis:{tooltip:!0},width:140},{title:()=>t("storage.capacity"),key:"sizeBytes",width:100,render:n=>re(n.sizeBytes)},{title:()=>t("storage.interface"),key:"interfaceType",width:70},{title:()=>t("storage.diskType"),key:"isSsd",width:60,render:n=>n.isSsd?"SSD":"HDD"},{title:()=>t("storage.smartStatus"),key:"smartStatus",width:90,render:n=>{var z,u;return m("span",{style:{color:(z=n.smartStatus)!=null&&z.toLowerCase().includes("ok")||(u=n.smartStatus)!=null&&u.toLowerCase().includes("pass")?"#4ade80":"#f87171"}},n.smartStatus??t("common.unknown"))}},{title:()=>t("storage.temperature"),key:"temperatureCelsius",width:70,render:n=>U(n.temperatureCelsius)},{title:()=>t("storage.usage"),key:"usedPercent",width:80,render:n=>ne(n.usedPercent)},{title:()=>t("common.actions"),key:"actions",width:100,render:n=>m(te,{size:"tiny",onClick:()=>_(n)},{default:()=>t("common.detail")})}];return(n,z)=>{const u=je,k=we,s=Ge,f=De,v=qe,y=Xe,w=Te,a=Me,j=Le,I=Ne,P=Be;return E(),ee("div",Qe,[b(ze,{title:d(t)("storage.title"),subtitle:d(t)("storage.subtitle")},{actions:g(()=>[b(d(te),{size:"small",loading:d(e).loading,onClick:z[0]||(z[0]=C=>d(e).fetchDisks())},{default:g(()=>[S($(d(t)("common.refresh")),1)]),_:1},8,["loading"])]),_:1},8,["title","subtitle"]),b(k,{title:d(t)("storage.diskList"),bordered:!1,size:"small"},{default:g(()=>[d(e).disks.length?(E(),A(u,{key:0,columns:p,data:d(e).disks,bordered:!1,size:"small",striped:"",loading:d(e).loading},null,8,["data","loading"])):(E(),A(Pe,{key:1,message:d(t)("storage.noDisks")},null,8,["message"]))]),_:1},8,["title"]),b(P,{show:h.value,"onUpdate:show":z[1]||(z[1]=C=>h.value=C),width:500,placement:"right"},{default:g(()=>[r.value?(E(),A(I,{key:0,title:d(t)("storage.diskDetail"),closable:""},{header:g(()=>[S($(r.value.model),1)]),default:g(()=>[b(v,{"label-placement":"left",column:1,bordered:"",size:"small"},{default:g(()=>[b(s,{label:d(t)("storage.devicePath")},{default:g(()=>[S($(r.value.path),1)]),_:1},8,["label"]),b(s,{label:d(t)("storage.model")},{default:g(()=>[S($(r.value.model),1)]),_:1},8,["label"]),b(s,{label:d(t)("storage.serial")},{default:g(()=>[S($(r.value.serial),1)]),_:1},8,["label"]),b(s,{label:d(t)("storage.capacity")},{default:g(()=>[S($(d(re)(r.value.sizeBytes)),1)]),_:1},8,["label"]),b(s,{label:d(t)("storage.interface")},{default:g(()=>[S($(r.value.interfaceType),1)]),_:1},8,["label"]),b(s,{label:d(t)("storage.diskType")},{default:g(()=>[b(f,{type:r.value.isSsd?"info":"default",size:"small"},{default:g(()=>[S($(r.value.isSsd?"SSD":"HDD"),1)]),_:1},8,["type"])]),_:1},8,["label"]),b(s,{label:d(t)("storage.smartStatus")},{default:g(()=>{var C,R;return[b(f,{type:(C=r.value.smartStatus)!=null&&C.toLowerCase().includes("ok")||(R=r.value.smartStatus)!=null&&R.toLowerCase().includes("pass")?"success":"error",size:"small"},{default:g(()=>[S($(r.value.smartStatus),1)]),_:1},8,["type"])]}),_:1},8,["label"]),b(s,{label:d(t)("storage.temperature")},{default:g(()=>[S($(d(U)(r.value.temperatureCelsius)),1)]),_:1},8,["label"]),b(s,{label:d(t)("storage.usage")},{default:g(()=>[S($(d(ne)(r.value.usedPercent)),1)]),_:1},8,["label"])]),_:1}),b(y),$e("h4",Ye,$(d(t)("storage.smartDetail")),1),c.value?(E(),A(w,{key:0})):l.value?(E(),ee("div",et,[b(v,{"label-placement":"left",column:1,bordered:"",size:"small"},{default:g(()=>[b(s,{label:d(t)("storage.healthStatus")},{default:g(()=>[S($(l.value.health),1)]),_:1},8,["label"]),b(s,{label:d(t)("storage.temperature")},{default:g(()=>[S($(d(U)(l.value.temperatureCelsius)),1)]),_:1},8,["label"])]),_:1}),l.value.rawJson?(E(),A(a,{key:0,code:l.value.rawJson,language:"json",style:{"margin-top":"12px","max-height":"400px"}},null,8,["code"])):oe("",!0)])):(E(),A(j,{key:2,type:"warning",style:{"margin-top":"8px"}},{default:g(()=>[S($(d(t)("storage.smartFailed")),1)]),_:1}))]),_:1},8,["title"])):oe("",!0)]),_:1},8,["show"])])}}}),pt=xe(tt,[["__scopeId","data-v-6a3ed7b2"]]);export{pt as default};

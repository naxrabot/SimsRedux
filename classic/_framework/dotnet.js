//! Licensed to the .NET Foundation under one or more agreements.
//! The .NET Foundation licenses this file to you under the MIT license.

var e=!1;const t=async()=>WebAssembly.validate(new Uint8Array([0,97,115,109,1,0,0,0,1,4,1,96,0,0,3,2,1,0,10,8,1,6,0,6,64,25,11,11])),o=async()=>WebAssembly.validate(new Uint8Array([0,97,115,109,1,0,0,0,1,5,1,96,0,1,123,3,2,1,0,10,15,1,13,0,65,1,253,15,65,2,253,15,253,128,2,11])),n=async()=>WebAssembly.validate(new Uint8Array([0,97,115,109,1,0,0,0,1,5,1,96,0,1,123,3,2,1,0,10,10,1,8,0,65,0,253,15,253,98,11])),r=Symbol.for("wasm promise_control");function i(e,t){let o=null;const n=new Promise((function(n,r){o={isDone:!1,promise:null,resolve:t=>{o.isDone||(o.isDone=!0,n(t),e&&e())},reject:e=>{o.isDone||(o.isDone=!0,r(e),t&&t())}}}));o.promise=n;const i=n;return i[r]=o,{promise:i,promise_control:o}}function s(e){return e[r]}function a(e){e&&function(e){return void 0!==e[r]}(e)||Be(!1,"Promise is not controllable")}const l="__mono_message__",c=["debug","log","trace","warn","info","error"],d="MONO_WASM: ";let u,f,m,g,p,h;function w(e){g=e}function b(e){if(Pe.diagnosticTracing){const t="function"==typeof e?e():e;console.debug(d+t)}}function y(e,...t){console.info(d+e,...t)}function v(e,...t){console.info(e,...t)}function E(e,...t){console.warn(d+e,...t)}function _(e,...t){if(t&&t.length>0&&t[0]&&"object"==typeof t[0]){if(t[0].silent)return;if(t[0].toString)return void console.error(d+e,t[0].toString())}console.error(d+e,...t)}function x(e,t,o){return function(...n){try{let r=n[0];if(void 0===r)r="undefined";else if(null===r)r="null";else if("function"==typeof r)r=r.toString();else if("string"!=typeof r)try{r=JSON.stringify(r)}catch(e){r=r.toString()}t(o?JSON.stringify({method:e,payload:r,arguments:n.slice(1)}):[e+r,...n.slice(1)])}catch(e){m.error(`proxyConsole failed: ${e}`)}}}function j(e,t,o){f=t,g=e,m={...t};const n=`${o}/console`.replace("https://","wss://").replace("http://","ws://");u=new WebSocket(n),u.addEventListener("error",A),u.addEventListener("close",S),function(){for(const e of c)f[e]=x(`console.${e}`,T,!0)}()}function R(e){let t=30;const o=()=>{u?0==u.bufferedAmount||0==t?(e&&v(e),function(){for(const e of c)f[e]=x(`console.${e}`,m.log,!1)}(),u.removeEventListener("error",A),u.removeEventListener("close",S),u.close(1e3,e),u=void 0):(t--,globalThis.setTimeout(o,100)):e&&m&&m.log(e)};o()}function T(e){u&&u.readyState===WebSocket.OPEN?u.send(e):m.log(e)}function A(e){m.error(`[${g}] proxy console websocket error: ${e}`,e)}function S(e){m.debug(`[${g}] proxy console websocket closed: ${e}`,e)}function D(){Pe.preferredIcuAsset=O(Pe.config);let e="invariant"==Pe.config.globalizationMode;if(!e)if(Pe.preferredIcuAsset)Pe.diagnosticTracing&&b("ICU data archive(s) available, disabling invariant mode");else{if("custom"===Pe.config.globalizationMode||"all"===Pe.config.globalizationMode||"sharded"===Pe.config.globalizationMode){const e="invariant globalization mode is inactive and no ICU data archives are available";throw _(`ERROR: ${e}`),new Error(e)}Pe.diagnosticTracing&&b("ICU data archive(s) not available, using invariant globalization mode"),e=!0,Pe.preferredIcuAsset=null}const t="DOTNET_SYSTEM_GLOBALIZATION_INVARIANT",o=Pe.config.environmentVariables;if(void 0===o[t]&&e&&(o[t]="1"),void 0===o.TZ)try{const e=Intl.DateTimeFormat().resolvedOptions().timeZone||null;e&&(o.TZ=e)}catch(e){y("failed to detect timezone, will fallback to UTC")}}function O(e){var t;if((null===(t=e.resources)||void 0===t?void 0:t.icu)&&"invariant"!=e.globalizationMode){const t=e.applicationCulture||(ke?globalThis.navigator&&globalThis.navigator.languages&&globalThis.navigator.languages[0]:Intl.DateTimeFormat().resolvedOptions().locale),o=e.resources.icu;let n=null;if("custom"===e.globalizationMode){if(o.length>=1)return o[0].name}else t&&"all"!==e.globalizationMode?"sharded"===e.globalizationMode&&(n=function(e){const t=e.split("-")[0];return"en"===t||["fr","fr-FR","it","it-IT","de","de-DE","es","es-ES"].includes(e)?"icudt_EFIGS.dat":["zh","ko","ja"].includes(t)?"icudt_CJK.dat":"icudt_no_CJK.dat"}(t)):n="icudt.dat";if(n)for(let e=0;e<o.length;e++){const t=o[e];if(t.virtualPath===n)return t.name}}return e.globalizationMode="invariant",null}(new Date).valueOf();const C=class{constructor(e){this.url=e}toString(){return this.url}};async function k(e,t){try{const o="function"==typeof globalThis.fetch;if(Se){const n=e.startsWith("file://");if(!n&&o)return globalThis.fetch(e,t||{credentials:"same-origin"});p||(h=Ne.require("url"),p=Ne.require("fs")),n&&(e=h.fileURLToPath(e));const r=await p.promises.readFile(e);return{ok:!0,headers:{length:0,get:()=>null},url:e,arrayBuffer:()=>r,json:()=>JSON.parse(r),text:()=>{throw new Error("NotImplementedException")}}}if(o)return globalThis.fetch(e,t||{credentials:"same-origin"});if("function"==typeof read)return{ok:!0,url:e,headers:{length:0,get:()=>null},arrayBuffer:()=>new Uint8Array(read(e,"binary")),json:()=>JSON.parse(read(e,"utf8")),text:()=>read(e,"utf8")}}catch(t){return{ok:!1,url:e,status:500,headers:{length:0,get:()=>null},statusText:"ERR28: "+t,arrayBuffer:()=>{throw t},json:()=>{throw t},text:()=>{throw t}}}throw new Error("No fetch implementation available")}function I(e){return"string"!=typeof e&&Be(!1,"url must be a string"),!M(e)&&0!==e.indexOf("./")&&0!==e.indexOf("../")&&globalThis.URL&&globalThis.document&&globalThis.document.baseURI&&(e=new URL(e,globalThis.document.baseURI).toString()),e}const U=/^[a-zA-Z][a-zA-Z\d+\-.]*?:\/\//,P=/[a-zA-Z]:[\\/]/;function M(e){return Se||Ie?e.startsWith("/")||e.startsWith("\\")||-1!==e.indexOf("///")||P.test(e):U.test(e)}let L,N=0;const $=[],z=[],W=new Map,F={"js-module-threads":!0,"js-module-runtime":!0,"js-module-dotnet":!0,"js-module-native":!0,"js-module-diagnostics":!0},B={...F,"js-module-library-initializer":!0},V={...F,dotnetwasm:!0,heap:!0,manifest:!0},q={...B,manifest:!0},H={...B,dotnetwasm:!0},J={dotnetwasm:!0,symbols:!0},Z={...B,dotnetwasm:!0,symbols:!0},Q={symbols:!0};function G(e){return!("icu"==e.behavior&&e.name!=Pe.preferredIcuAsset)}function K(e,t,o){null!=t||(t=[]),Be(1==t.length,`Expect to have one ${o} asset in resources`);const n=t[0];return n.behavior=o,X(n),e.push(n),n}function X(e){V[e.behavior]&&W.set(e.behavior,e)}function Y(e){Be(V[e],`Unknown single asset behavior ${e}`);const t=W.get(e);if(t&&!t.resolvedUrl)if(t.resolvedUrl=Pe.locateFile(t.name),F[t.behavior]){const e=ge(t);e?("string"!=typeof e&&Be(!1,"loadBootResource response for 'dotnetjs' type should be a URL string"),t.resolvedUrl=e):t.resolvedUrl=ce(t.resolvedUrl,t.behavior)}else if("dotnetwasm"!==t.behavior)throw new Error(`Unknown single asset behavior ${e}`);return t}function ee(e){const t=Y(e);return Be(t,`Single asset for ${e} not found`),t}let te=!1;async function oe(){if(!te){te=!0,Pe.diagnosticTracing&&b("mono_download_assets");try{const e=[],t=[],o=(e,t)=>{!Z[e.behavior]&&G(e)&&Pe.expected_instantiated_assets_count++,!H[e.behavior]&&G(e)&&(Pe.expected_downloaded_assets_count++,t.push(se(e)))};for(const t of $)o(t,e);for(const e of z)o(e,t);Pe.allDownloadsQueued.promise_control.resolve(),Promise.all([...e,...t]).then((()=>{Pe.allDownloadsFinished.promise_control.resolve()})).catch((e=>{throw Pe.err("Error in mono_download_assets: "+e),Xe(1,e),e})),await Pe.runtimeModuleLoaded.promise;const n=async e=>{const t=await e;if(t.buffer){if(!Z[t.behavior]){t.buffer&&"object"==typeof t.buffer||Be(!1,"asset buffer must be array-like or buffer-like or promise of these"),"string"!=typeof t.resolvedUrl&&Be(!1,"resolvedUrl must be string");const e=t.resolvedUrl,o=await t.buffer,n=new Uint8Array(o);pe(t),await Ue.beforeOnRuntimeInitialized.promise,Ue.instantiate_asset(t,e,n)}}else J[t.behavior]?("symbols"===t.behavior&&(await Ue.instantiate_symbols_asset(t),pe(t)),J[t.behavior]&&++Pe.actual_downloaded_assets_count):(t.isOptional||Be(!1,"Expected asset to have the downloaded buffer"),!H[t.behavior]&&G(t)&&Pe.expected_downloaded_assets_count--,!Z[t.behavior]&&G(t)&&Pe.expected_instantiated_assets_count--)},r=[],i=[];for(const t of e)r.push(n(t));for(const e of t)i.push(n(e));Promise.all(r).then((()=>{Ce||Ue.coreAssetsInMemory.promise_control.resolve()})).catch((e=>{throw Pe.err("Error in mono_download_assets: "+e),Xe(1,e),e})),Promise.all(i).then((async()=>{Ce||(await Ue.coreAssetsInMemory.promise,Ue.allAssetsInMemory.promise_control.resolve())})).catch((e=>{throw Pe.err("Error in mono_download_assets: "+e),Xe(1,e),e}))}catch(e){throw Pe.err("Error in mono_download_assets: "+e),e}}}let ne=!1;function re(){if(ne)return;ne=!0;const e=Pe.config,t=[];if(e.assets)for(const t of e.assets)"object"!=typeof t&&Be(!1,`asset must be object, it was ${typeof t} : ${t}`),"string"!=typeof t.behavior&&Be(!1,"asset behavior must be known string"),"string"!=typeof t.name&&Be(!1,"asset name must be string"),t.resolvedUrl&&"string"!=typeof t.resolvedUrl&&Be(!1,"asset resolvedUrl could be string"),t.hash&&"string"!=typeof t.hash&&Be(!1,"asset resolvedUrl could be string"),t.pendingDownload&&"object"!=typeof t.pendingDownload&&Be(!1,"asset pendingDownload could be object"),t.isCore?$.push(t):z.push(t),X(t);else if(e.resources){const o=e.resources;o.wasmNative||Be(!1,"resources.wasmNative must be defined"),o.jsModuleNative||Be(!1,"resources.jsModuleNative must be defined"),o.jsModuleRuntime||Be(!1,"resources.jsModuleRuntime must be defined"),K(z,o.wasmNative,"dotnetwasm"),K(t,o.jsModuleNative,"js-module-native"),K(t,o.jsModuleRuntime,"js-module-runtime"),o.jsModuleDiagnostics&&K(t,o.jsModuleDiagnostics,"js-module-diagnostics");const n=(e,t,o)=>{const n=e;n.behavior=t,o?(n.isCore=!0,$.push(n)):z.push(n)};if(o.coreAssembly)for(let e=0;e<o.coreAssembly.length;e++)n(o.coreAssembly[e],"assembly",!0);if(o.assembly)for(let e=0;e<o.assembly.length;e++)n(o.assembly[e],"assembly",!o.coreAssembly);if(0!=e.debugLevel&&Pe.isDebuggingSupported()){if(o.corePdb)for(let e=0;e<o.corePdb.length;e++)n(o.corePdb[e],"pdb",!0);if(o.pdb)for(let e=0;e<o.pdb.length;e++)n(o.pdb[e],"pdb",!o.corePdb)}if(e.loadAllSatelliteResources&&o.satelliteResources)for(const e in o.satelliteResources)for(let t=0;t<o.satelliteResources[e].length;t++){const r=o.satelliteResources[e][t];r.culture=e,n(r,"resource",!o.coreAssembly)}if(o.coreVfs)for(let e=0;e<o.coreVfs.length;e++)n(o.coreVfs[e],"vfs",!0);if(o.vfs)for(let e=0;e<o.vfs.length;e++)n(o.vfs[e],"vfs",!o.coreVfs);const r=O(e);if(r&&o.icu)for(let e=0;e<o.icu.length;e++){const t=o.icu[e];t.name===r&&n(t,"icu",!1)}if(o.wasmSymbols)for(let e=0;e<o.wasmSymbols.length;e++)n(o.wasmSymbols[e],"symbols",!1)}if(e.appsettings)for(let t=0;t<e.appsettings.length;t++){const o=e.appsettings[t],n=he(o);"appsettings.json"!==n&&n!==`appsettings.${e.applicationEnvironment}.json`||z.push({name:o,behavior:"vfs",cache:"no-cache",useCredentials:!0})}e.assets=[...$,...z,...t]}async function ie(e){const t=await se(e);return await t.pendingDownloadInternal.response,t.buffer}async function se(e){try{return await ae(e)}catch(t){if(!Pe.enableDownloadRetry)throw t;if(Ie||Se)throw t;if(e.pendingDownload&&e.pendingDownloadInternal==e.pendingDownload)throw t;if(e.resolvedUrl&&-1!=e.resolvedUrl.indexOf("file://"))throw t;if(t&&404==t.status)throw t;e.pendingDownloadInternal=void 0,await Pe.allDownloadsQueued.promise;try{return Pe.diagnosticTracing&&b(`Retrying download '${e.name}'`),await ae(e)}catch(t){return e.pendingDownloadInternal=void 0,await new Promise((e=>globalThis.setTimeout(e,100))),Pe.diagnosticTracing&&b(`Retrying download (2) '${e.name}' after delay`),await ae(e)}}}async function ae(e){for(;L;)await L.promise;try{++N,N==Pe.maxParallelDownloads&&(Pe.diagnosticTracing&&b("Throttling further parallel downloads"),L=i());const t=await async function(e){if(e.pendingDownload&&(e.pendingDownloadInternal=e.pendingDownload),e.pendingDownloadInternal&&e.pendingDownloadInternal.response)return e.pendingDownloadInternal.response;if(e.buffer){const t=await e.buffer;return e.resolvedUrl||(e.resolvedUrl="undefined://"+e.name),e.pendingDownloadInternal={url:e.resolvedUrl,name:e.name,response:Promise.resolve({ok:!0,arrayBuffer:()=>t,json:()=>JSON.parse(new TextDecoder("utf-8").decode(t)),text:()=>{throw new Error("NotImplementedException")},headers:{get:()=>{}}})},e.pendingDownloadInternal.response}const t=e.loadRemote&&Pe.config.remoteSources?Pe.config.remoteSources:[""];let o;for(let n of t){n=n.trim(),"./"===n&&(n="");const t=le(e,n);e.name===t?Pe.diagnosticTracing&&b(`Attempting to download '${t}'`):Pe.diagnosticTracing&&b(`Attempting to download '${t}' for ${e.name}`);try{e.resolvedUrl=t;const n=fe(e);if(e.pendingDownloadInternal=n,o=await n.response,!o||!o.ok)continue;return o}catch(e){o||(o={ok:!1,url:t,status:0,statusText:""+e});continue}}const n=e.isOptional||e.name.match(/\.pdb$/)&&Pe.config.ignorePdbLoadErrors;if(o||Be(!1,`Response undefined ${e.name}`),!n){const t=new Error(`download '${o.url}' for ${e.name} failed ${o.status} ${o.statusText}`);throw t.status=o.status,t}y(`optional download '${o.url}' for ${e.name} failed ${o.status} ${o.statusText}`)}(e);return t?(J[e.behavior]||(e.buffer=await t.arrayBuffer(),++Pe.actual_downloaded_assets_count),e):e}finally{if(--N,L&&N==Pe.maxParallelDownloads-1){Pe.diagnosticTracing&&b("Resuming more parallel downloads");const e=L;L=void 0,e.promise_control.resolve()}}}function le(e,t){let o;return null==t&&Be(!1,`sourcePrefix must be provided for ${e.name}`),e.resolvedUrl?o=e.resolvedUrl:(o=""===t?"assembly"===e.behavior||"pdb"===e.behavior?e.name:"resource"===e.behavior&&e.culture&&""!==e.culture?`${e.culture}/${e.name}`:e.name:t+e.name,o=ce(Pe.locateFile(o),e.behavior)),o&&"string"==typeof o||Be(!1,"attemptUrl need to be path or url string"),o}function ce(e,t){return Pe.modulesUniqueQuery&&q[t]&&(e+=Pe.modulesUniqueQuery),e}let de=0;const ue=new Set;function fe(e){try{e.resolvedUrl||Be(!1,"Request's resolvedUrl must be set");const t=function(e){let t=e.resolvedUrl;if(Pe.loadBootResource){const o=ge(e);if(o instanceof Promise)return o;"string"==typeof o&&(t=o)}const o={};return e.cache?o.cache=e.cache:Pe.config.disableNoCacheFetch||(o.cache="no-cache"),e.useCredentials?o.credentials="include":!Pe.config.disableIntegrityCheck&&e.hash&&(o.integrity=e.hash),Pe.fetch_like(t,o)}(e),o={name:e.name,url:e.resolvedUrl,response:t};return ue.add(e.name),o.response.then((()=>{"assembly"==e.behavior&&Pe.loadedAssemblies.push(e.name),de++,Pe.onDownloadResourceProgress&&Pe.onDownloadResourceProgress(de,ue.size)})),o}catch(t){const o={ok:!1,url:e.resolvedUrl,status:500,statusText:"ERR29: "+t,arrayBuffer:()=>{throw t},json:()=>{throw t}};return{name:e.name,url:e.resolvedUrl,response:Promise.resolve(o)}}}const me={resource:"assembly",assembly:"assembly",pdb:"pdb",icu:"globalization",vfs:"configuration",manifest:"manifest",dotnetwasm:"dotnetwasm","js-module-dotnet":"dotnetjs","js-module-native":"dotnetjs","js-module-runtime":"dotnetjs","js-module-threads":"dotnetjs"};function ge(e){var t;if(Pe.loadBootResource){const o=null!==(t=e.hash)&&void 0!==t?t:"",n=e.resolvedUrl,r=me[e.behavior];if(r){const t=Pe.loadBootResource(r,e.name,n,o,e.behavior);return"string"==typeof t?I(t):t}}}function pe(e){e.pendingDownloadInternal=null,e.pendingDownload=null,e.buffer=null,e.moduleExports=null}function he(e){let t=e.lastIndexOf("/");return t>=0&&t++,e.substring(t)}async function we(e){e&&await Promise.all((null!=e?e:[]).map((e=>async function(e){try{const t=e.name;if(!e.moduleExports){const o=ce(Pe.locateFile(t),"js-module-library-initializer");Pe.diagnosticTracing&&b(`Attempting to import '${o}' for ${e}`),e.moduleExports=await import(/*! webpackIgnore: true */o)}Pe.libraryInitializers.push({scriptName:t,exports:e.moduleExports})}catch(t){E(`Failed to import library initializer '${e}': ${t}`)}}(e))))}async function be(e,t){if(!Pe.libraryInitializers)return;const o=[];for(let n=0;n<Pe.libraryInitializers.length;n++){const r=Pe.libraryInitializers[n];r.exports[e]&&o.push(ye(r.scriptName,e,(()=>r.exports[e](...t))))}await Promise.all(o)}async function ye(e,t,o){try{await o()}catch(o){throw E(`Failed to invoke '${t}' on library initializer '${e}': ${o}`),Xe(1,o),o}}function ve(e,t){if(e===t)return e;const o={...t};return void 0!==o.assets&&o.assets!==e.assets&&(o.assets=[...e.assets||[],...o.assets||[]]),void 0!==o.resources&&(o.resources=_e(e.resources||{assembly:[],jsModuleNative:[],jsModuleRuntime:[],wasmNative:[]},o.resources)),void 0!==o.environmentVariables&&(o.environmentVariables={...e.environmentVariables||{},...o.environmentVariables||{}}),void 0!==o.runtimeOptions&&o.runtimeOptions!==e.runtimeOptions&&(o.runtimeOptions=[...e.runtimeOptions||[],...o.runtimeOptions||[]]),Object.assign(e,o)}function Ee(e,t){if(e===t)return e;const o={...t};return o.config&&(e.config||(e.config={}),o.config=ve(e.config,o.config)),Object.assign(e,o)}function _e(e,t){if(e===t)return e;const o={...t};return void 0!==o.coreAssembly&&(o.coreAssembly=[...e.coreAssembly||[],...o.coreAssembly||[]]),void 0!==o.assembly&&(o.assembly=[...e.assembly||[],...o.assembly||[]]),void 0!==o.lazyAssembly&&(o.lazyAssembly=[...e.lazyAssembly||[],...o.lazyAssembly||[]]),void 0!==o.corePdb&&(o.corePdb=[...e.corePdb||[],...o.corePdb||[]]),void 0!==o.pdb&&(o.pdb=[...e.pdb||[],...o.pdb||[]]),void 0!==o.jsModuleWorker&&(o.jsModuleWorker=[...e.jsModuleWorker||[],...o.jsModuleWorker||[]]),void 0!==o.jsModuleNative&&(o.jsModuleNative=[...e.jsModuleNative||[],...o.jsModuleNative||[]]),void 0!==o.jsModuleDiagnostics&&(o.jsModuleDiagnostics=[...e.jsModuleDiagnostics||[],...o.jsModuleDiagnostics||[]]),void 0!==o.jsModuleRuntime&&(o.jsModuleRuntime=[...e.jsModuleRuntime||[],...o.jsModuleRuntime||[]]),void 0!==o.wasmSymbols&&(o.wasmSymbols=[...e.wasmSymbols||[],...o.wasmSymbols||[]]),void 0!==o.wasmNative&&(o.wasmNative=[...e.wasmNative||[],...o.wasmNative||[]]),void 0!==o.icu&&(o.icu=[...e.icu||[],...o.icu||[]]),void 0!==o.satelliteResources&&(o.satelliteResources=function(e,t){if(e===t)return e;for(const o in t)e[o]=[...e[o]||[],...t[o]||[]];return e}(e.satelliteResources||{},o.satelliteResources||{})),void 0!==o.modulesAfterConfigLoaded&&(o.modulesAfterConfigLoaded=[...e.modulesAfterConfigLoaded||[],...o.modulesAfterConfigLoaded||[]]),void 0!==o.modulesAfterRuntimeReady&&(o.modulesAfterRuntimeReady=[...e.modulesAfterRuntimeReady||[],...o.modulesAfterRuntimeReady||[]]),void 0!==o.extensions&&(o.extensions={...e.extensions||{},...o.extensions||{}}),void 0!==o.vfs&&(o.vfs=[...e.vfs||[],...o.vfs||[]]),Object.assign(e,o)}function xe(){const e=Pe.config;if(e.environmentVariables=e.environmentVariables||{},e.runtimeOptions=e.runtimeOptions||[],e.resources=e.resources||{assembly:[],jsModuleNative:[],jsModuleWorker:[],jsModuleRuntime:[],wasmNative:[],vfs:[],satelliteResources:{}},e.assets){Pe.diagnosticTracing&&b("config.assets is deprecated, use config.resources instead");for(const t of e.assets){const o={};switch(t.behavior){case"assembly":o.assembly=[t];break;case"pdb":o.pdb=[t];break;case"resource":o.satelliteResources={},o.satelliteResources[t.culture]=[t];break;case"icu":o.icu=[t];break;case"symbols":o.wasmSymbols=[t];break;case"vfs":o.vfs=[t];break;case"dotnetwasm":o.wasmNative=[t];break;case"js-module-threads":o.jsModuleWorker=[t];break;case"js-module-runtime":o.jsModuleRuntime=[t];break;case"js-module-native":o.jsModuleNative=[t];break;case"js-module-diagnostics":o.jsModuleDiagnostics=[t];break;case"js-module-dotnet":break;default:throw new Error(`Unexpected behavior ${t.behavior} of asset ${t.name}`)}_e(e.resources,o)}}e.debugLevel,e.applicationEnvironment||(e.applicationEnvironment="Production"),e.applicationCulture&&(e.environmentVariables.LANG=`${e.applicationCulture}.UTF-8`),Ue.diagnosticTracing=Pe.diagnosticTracing=!!e.diagnosticTracing,Ue.waitForDebugger=e.waitForDebugger,Pe.maxParallelDownloads=e.maxParallelDownloads||Pe.maxParallelDownloads,Pe.enableDownloadRetry=void 0!==e.enableDownloadRetry?e.enableDownloadRetry:Pe.enableDownloadRetry}let je=!1;async function Re(e){var t;if(je)return void await Pe.afterConfigLoaded.promise;let o;try{if(e.configSrc||Pe.config&&0!==Object.keys(Pe.config).length&&(Pe.config.assets||Pe.config.resources)||(e.configSrc="dotnet.boot.js"),o=e.configSrc,je=!0,o&&(Pe.diagnosticTracing&&b("mono_wasm_load_config"),await async function(e){const t=e.configSrc,o=Pe.locateFile(t);let n=null;void 0!==Pe.loadBootResource&&(n=Pe.loadBootResource("manifest",t,o,"","manifest"));let r,i=null;if(n)if("string"==typeof n)n.includes(".json")?(i=await s(I(n)),r=await Ae(i)):r=(await import(I(n))).config;else{const e=await n;"function"==typeof e.json?(i=e,r=await Ae(i)):r=e.config}else o.includes(".json")?(i=await s(ce(o,"manifest")),r=await Ae(i)):r=(await import(ce(o,"manifest"))).config;function s(e){return Pe.fetch_like(e,{method:"GET",credentials:"include",cache:"no-cache"})}Pe.config.applicationEnvironment&&(r.applicationEnvironment=Pe.config.applicationEnvironment),ve(Pe.config,r)}(e)),xe(),await we(null===(t=Pe.config.resources)||void 0===t?void 0:t.modulesAfterConfigLoaded),await be("onRuntimeConfigLoaded",[Pe.config]),e.onConfigLoaded)try{await e.onConfigLoaded(Pe.config,Le),xe()}catch(e){throw _("onConfigLoaded() failed",e),e}xe(),Pe.afterConfigLoaded.promise_control.resolve(Pe.config)}catch(t){const n=`Failed to load config file ${o} ${t} ${null==t?void 0:t.stack}`;throw Pe.config=e.config=Object.assign(Pe.config,{message:n,error:t,isError:!0}),Xe(1,new Error(n)),t}}function Te(){return!!globalThis.navigator&&(Pe.isChromium||Pe.isFirefox)}async function Ae(e){const t=Pe.config,o=await e.json();t.applicationEnvironment||o.applicationEnvironment||(o.applicationEnvironment=e.headers.get("Blazor-Environment")||e.headers.get("DotNet-Environment")||void 0),o.environmentVariables||(o.environmentVariables={});const n=e.headers.get("DOTNET-MODIFIABLE-ASSEMBLIES");n&&(o.environmentVariables.DOTNET_MODIFIABLE_ASSEMBLIES=n);const r=e.headers.get("ASPNETCORE-BROWSER-TOOLS");return r&&(o.environmentVariables.__ASPNETCORE_BROWSER_TOOLS=r),o}"function"!=typeof importScripts||globalThis.onmessage||(globalThis.dotnetSidecar=!0);const Se="object"==typeof process&&"object"==typeof process.versions&&"string"==typeof process.versions.node,De="function"==typeof importScripts,Oe=De&&"undefined"!=typeof dotnetSidecar,Ce=De&&!Oe,ke="object"==typeof window||De&&!Se,Ie=!ke&&!Se;let Ue={},Pe={},Me={},Le={},Ne={},$e=!1;const ze={},We={config:ze},Fe={mono:{},binding:{},internal:Ne,module:We,loaderHelpers:Pe,runtimeHelpers:Ue,diagnosticHelpers:Me,api:Le};function Be(e,t){if(e)return;const o="Assert failed: "+("function"==typeof t?t():t),n=new Error(o);_(o,n),Ue.nativeAbort(n)}function Ve(){return void 0!==Pe.exitCode}function qe(){return Ue.runtimeReady&&!Ve()}function He(){Ve()&&Be(!1,`.NET runtime already exited with ${Pe.exitCode} ${Pe.exitReason}. You can use runtime.runMain() which doesn't exit the runtime.`),Ue.runtimeReady||Be(!1,".NET runtime didn't start yet. Please call dotnet.create() first.")}function Je(){ke&&(globalThis.addEventListener("unhandledrejection",et),globalThis.addEventListener("error",tt))}let Ze,Qe;function Ge(e){Qe&&Qe(e),Xe(e,Pe.exitReason)}function Ke(e){Ze&&Ze(e||Pe.exitReason),Xe(1,e||Pe.exitReason)}function Xe(t,o){var n,r;const i=o&&"object"==typeof o;t=i&&"number"==typeof o.status?o.status:void 0===t?-1:t;const s=i&&"string"==typeof o.message?o.message:""+o;(o=i?o:Ue.ExitStatus?function(e,t){const o=new Ue.ExitStatus(e);return o.message=t,o.toString=()=>t,o}(t,s):new Error("Exit with code "+t+" "+s)).status=t,o.message||(o.message=s);const a=""+(o.stack||(new Error).stack);try{Object.defineProperty(o,"stack",{get:()=>a})}catch(e){}const l=!!o.silent;if(o.silent=!0,Ve())Pe.diagnosticTracing&&b("mono_exit called after exit");else{try{We.onAbort==Ke&&(We.onAbort=Ze),We.onExit==Ge&&(We.onExit=Qe),ke&&(globalThis.removeEventListener("unhandledrejection",et),globalThis.removeEventListener("error",tt)),Ue.runtimeReady?(Ue.jiterpreter_dump_stats&&Ue.jiterpreter_dump_stats(!1),0===t&&(null===(n=Pe.config)||void 0===n?void 0:n.interopCleanupOnExit)&&Ue.forceDisposeProxies(!0,!0),e&&0!==t&&(null===(r=Pe.config)||void 0===r||r.dumpThreadsOnNonZeroExit)):(Pe.diagnosticTracing&&b(`abort_startup, reason: ${o}`),function(e){Pe.allDownloadsQueued.promise_control.reject(e),Pe.allDownloadsFinished.promise_control.reject(e),Pe.afterConfigLoaded.promise_control.reject(e),Pe.wasmCompilePromise.promise_control.reject(e),Pe.runtimeModuleLoaded.promise_control.reject(e),Ue.dotnetReady&&(Ue.dotnetReady.promise_control.reject(e),Ue.afterInstantiateWasm.promise_control.reject(e),Ue.beforePreInit.promise_control.reject(e),Ue.afterPreInit.promise_control.reject(e),Ue.afterPreRun.promise_control.reject(e),Ue.beforeOnRuntimeInitialized.promise_control.reject(e),Ue.afterOnRuntimeInitialized.promise_control.reject(e),Ue.afterPostRun.promise_control.reject(e))}(o))}catch(e){E("mono_exit A failed",e)}try{l||(function(e,t){if(0!==e&&t){const e=Ue.ExitStatus&&t instanceof Ue.ExitStatus?b:_;"string"==typeof t?e(t):(void 0===t.stack&&(t.stack=(new Error).stack+""),t.message?e(Ue.stringify_as_error_with_stack?Ue.stringify_as_error_with_stack(t.message+"\n"+t.stack):t.message+"\n"+t.stack):e(JSON.stringify(t)))}!Ce&&Pe.config&&(Pe.config.logExitCode?Pe.config.forwardConsoleLogsToWS?R("WASM EXIT "+e):v("WASM EXIT "+e):Pe.config.forwardConsoleLogsToWS&&R())}(t,o),function(e){if(ke&&!Ce&&Pe.config&&Pe.config.appendElementOnExit&&document){const t=document.createElement("label");t.id="tests_done",0!==e&&(t.style.background="red"),t.innerHTML=""+e,document.body.appendChild(t)}}(t))}catch(e){E("mono_exit B failed",e)}Pe.exitCode=t,Pe.exitReason||(Pe.exitReason=o),!Ce&&Ue.runtimeReady&&We.runtimeKeepalivePop()}if(Pe.config&&Pe.config.asyncFlushOnExit&&0===t)throw(async()=>{try{await async function(){try{const e=await import(/*! webpackIgnore: true */"process"),t=e=>new Promise(((t,o)=>{e.on("error",o),e.end("","utf8",t)})),o=t(e.stderr),n=t(e.stdout);let r;const i=new Promise((e=>{r=setTimeout((()=>e("timeout")),1e3)}));await Promise.race([Promise.all([n,o]),i]),clearTimeout(r)}catch(e){_(`flushing std* streams failed: ${e}`)}}()}finally{Ye(t,o)}})(),o;Ye(t,o)}function Ye(e,t){if(Ue.runtimeReady&&Ue.nativeExit)try{Ue.nativeExit(e)}catch(e){!Ue.ExitStatus||e instanceof Ue.ExitStatus||E("set_exit_code_and_quit_now failed: "+e.toString())}if(0!==e||!ke)throw Se&&Ne.process?Ne.process.exit(e):Ue.quit&&Ue.quit(e,t),t}function et(e){ot(e,e.reason,"rejection")}function tt(e){ot(e,e.error,"error")}function ot(e,t,o){e.preventDefault();try{t||(t=new Error("Unhandled "+o)),void 0===t.stack&&(t.stack=(new Error).stack),t.stack=t.stack+"",t.silent||(_("Unhandled error:",t),Xe(1,t))}catch(e){}}!function(e){if($e)throw new Error("Loader module already loaded");$e=!0,Ue=e.runtimeHelpers,Pe=e.loaderHelpers,Me=e.diagnosticHelpers,Le=e.api,Ne=e.internal,Object.assign(Le,{INTERNAL:Ne,invokeLibraryInitializers:be}),Object.assign(e.module,{config:ve(ze,{environmentVariables:{}})});const r={mono_wasm_bindings_is_ready:!1,config:e.module.config,diagnosticTracing:!1,nativeAbort:e=>{throw e||new Error("abort")},nativeExit:e=>{throw new Error("exit:"+e)}},l={gitHash:"f3c422da807f2a8698a7b1b95cb1badd4572ac4c",config:e.module.config,diagnosticTracing:!1,maxParallelDownloads:16,enableDownloadRetry:!0,_loaded_files:[],loadedFiles:[],loadedAssemblies:[],libraryInitializers:[],workerNextNumber:1,actual_downloaded_assets_count:0,actual_instantiated_assets_count:0,expected_downloaded_assets_count:0,expected_instantiated_assets_count:0,afterConfigLoaded:i(),allDownloadsQueued:i(),allDownloadsFinished:i(),wasmCompilePromise:i(),runtimeModuleLoaded:i(),loadingWorkers:i(),is_exited:Ve,is_runtime_running:qe,assert_runtime_running:He,mono_exit:Xe,createPromiseController:i,getPromiseController:s,assertIsControllablePromise:a,mono_download_assets:oe,resolve_single_asset_path:ee,setup_proxy_console:j,set_thread_prefix:w,installUnhandledErrorHandler:Je,retrieve_asset_download:ie,invokeLibraryInitializers:be,isDebuggingSupported:Te,exceptions:t,simd:n,relaxedSimd:o};Object.assign(Ue,r),Object.assign(Pe,l)}(Fe);let nt,rt,it,st=!1,at=!1;async function lt(e){if(!at){if(at=!0,ke&&Pe.config.forwardConsoleLogsToWS&&void 0!==globalThis.WebSocket&&j("main",globalThis.console,globalThis.location.origin),We||Be(!1,"Null moduleConfig"),Pe.config||Be(!1,"Null moduleConfig.config"),"function"==typeof e){const t=e(Fe.api);if(t.ready)throw new Error("Module.ready couldn't be redefined.");Object.assign(We,t),Ee(We,t)}else{if("object"!=typeof e)throw new Error("Can't use moduleFactory callback of createDotnetRuntime function.");Ee(We,e)}await async function(e){if(Se){const e=await import(/*! webpackIgnore: true */"process"),t=14;if(e.versions.node.split(".")[0]<t)throw new Error(`NodeJS at '${e.execPath}' has too low version '${e.versions.node}', please use at least ${t}. See also https://aka.ms/dotnet-wasm-features`)}const t=/*! webpackIgnore: true */import.meta.url,o=t.indexOf("?");var n;if(o>0&&(Pe.modulesUniqueQuery=t.substring(o)),Pe.scriptUrl=t.replace(/\\/g,"/").replace(/[?#].*/,""),Pe.scriptDirectory=(n=Pe.scriptUrl).slice(0,n.lastIndexOf("/"))+"/",Pe.locateFile=e=>"URL"in globalThis&&globalThis.URL!==C?new URL(e,Pe.scriptDirectory).toString():M(e)?e:Pe.scriptDirectory+e,Pe.fetch_like=k,Pe.out=console.log,Pe.err=console.error,Pe.onDownloadResourceProgress=e.onDownloadResourceProgress,ke&&globalThis.navigator){const e=globalThis.navigator,t=e.userAgentData&&e.userAgentData.brands;t&&t.length>0?Pe.isChromium=t.some((e=>"Google Chrome"===e.brand||"Microsoft Edge"===e.brand||"Chromium"===e.brand)):e.userAgent&&(Pe.isChromium=e.userAgent.includes("Chrome"),Pe.isFirefox=e.userAgent.includes("Firefox"))}Ne.require=Se?await import(/*! webpackIgnore: true */"module").then((e=>e.createRequire(/*! webpackIgnore: true */import.meta.url))):Promise.resolve((()=>{throw new Error("require not supported")})),void 0===globalThis.URL&&(globalThis.URL=C)}(We)}}async function ct(e){return await lt(e),Ze=We.onAbort,Qe=We.onExit,We.onAbort=Ke,We.onExit=Ge,We.ENVIRONMENT_IS_PTHREAD?async function(){(function(){const e=new MessageChannel,t=e.port1,o=e.port2;t.addEventListener("message",(e=>{var n,r;n=JSON.parse(e.data.config),r=JSON.parse(e.data.monoThreadInfo),st?Pe.diagnosticTracing&&b("mono config already received"):(ve(Pe.config,n),Ue.monoThreadInfo=r,xe(),Pe.diagnosticTracing&&b("mono config received"),st=!0,Pe.afterConfigLoaded.promise_control.resolve(Pe.config),ke&&n.forwardConsoleLogsToWS&&void 0!==globalThis.WebSocket&&Pe.setup_proxy_console("worker-idle",console,globalThis.location.origin)),t.close(),o.close()}),{once:!0}),t.start(),self.postMessage({[l]:{monoCmd:"preload",port:o}},[o])})(),await Pe.afterConfigLoaded.promise,function(){const e=Pe.config;e.assets||Be(!1,"config.assets must be defined");for(const t of e.assets)X(t),Q[t.behavior]&&z.push(t)}(),setTimeout((async()=>{try{await oe()}catch(e){Xe(1,e)}}),0);const e=dt(),t=await Promise.all(e);return await ut(t),We}():async function(){var e;await Re(We),re();const t=dt();(async function(){try{const e=ee("dotnetwasm");await se(e),e&&e.pendingDownloadInternal&&e.pendingDownloadInternal.response||Be(!1,"Can't load dotnet.native.wasm");const t=await e.pendingDownloadInternal.response,o=t.headers&&t.headers.get?t.headers.get("Content-Type"):void 0;let n;if("function"==typeof WebAssembly.compileStreaming&&"application/wasm"===o)n=await WebAssembly.compileStreaming(t);else{ke&&"application/wasm"!==o&&E('WebAssembly resource does not have the expected content type "application/wasm", so falling back to slower ArrayBuffer instantiation.');const e=await t.arrayBuffer();Pe.diagnosticTracing&&b("instantiate_wasm_module buffered"),n=Ie?await Promise.resolve(new WebAssembly.Module(e)):await WebAssembly.compile(e)}e.pendingDownloadInternal=null,e.pendingDownload=null,e.buffer=null,e.moduleExports=null,Pe.wasmCompilePromise.promise_control.resolve(n)}catch(e){Pe.wasmCompilePromise.promise_control.reject(e)}})(),setTimeout((async()=>{try{D(),await oe()}catch(e){Xe(1,e)}}),0);const o=await Promise.all(t);return await ut(o),await Ue.dotnetReady.promise,await we(null===(e=Pe.config.resources)||void 0===e?void 0:e.modulesAfterRuntimeReady),await be("onRuntimeReady",[Fe.api]),Le}()}function dt(){const e=ee("js-module-runtime"),t=ee("js-module-native");if(nt&&rt)return[nt,rt,it];"object"==typeof e.moduleExports?nt=e.moduleExports:(Pe.diagnosticTracing&&b(`Attempting to import '${e.resolvedUrl}' for ${e.name}`),nt=import(/*! webpackIgnore: true */e.resolvedUrl)),"object"==typeof t.moduleExports?rt=t.moduleExports:(Pe.diagnosticTracing&&b(`Attempting to import '${t.resolvedUrl}' for ${t.name}`),rt=import(/*! webpackIgnore: true */t.resolvedUrl));const o=Y("js-module-diagnostics");return o&&("object"==typeof o.moduleExports?it=o.moduleExports:(Pe.diagnosticTracing&&b(`Attempting to import '${o.resolvedUrl}' for ${o.name}`),it=import(/*! webpackIgnore: true */o.resolvedUrl))),[nt,rt,it]}async function ut(e){const{initializeExports:t,initializeReplacements:o,configureRuntimeStartup:n,configureEmscriptenStartup:r,configureWorkerStartup:i,setRuntimeGlobals:s,passEmscriptenInternals:a}=e[0],{default:l}=e[1],c=e[2];s(Fe),t(Fe),c&&c.setRuntimeGlobals(Fe),await n(We),Pe.runtimeModuleLoaded.promise_control.resolve(),l((e=>(Object.assign(We,{ready:e.ready,__dotnet_runtime:{initializeReplacements:o,configureEmscriptenStartup:r,configureWorkerStartup:i,passEmscriptenInternals:a}}),We))).catch((e=>{if(e.message&&e.message.toLowerCase().includes("out of memory"))throw new Error(".NET runtime has failed to start, because too much memory was requested. Please decrease the memory by adjusting EmccMaximumHeapSize. See also https://aka.ms/dotnet-wasm-features");throw e}))}const ft=new class{withModuleConfig(e){try{return Ee(We,e),this}catch(e){throw Xe(1,e),e}}withOnConfigLoaded(e){try{return Ee(We,{onConfigLoaded:e}),this}catch(e){throw Xe(1,e),e}}withConsoleForwarding(){try{return ve(ze,{forwardConsoleLogsToWS:!0}),this}catch(e){throw Xe(1,e),e}}withExitOnUnhandledError(){try{return ve(ze,{exitOnUnhandledError:!0}),Je(),this}catch(e){throw Xe(1,e),e}}withAsyncFlushOnExit(){try{return ve(ze,{asyncFlushOnExit:!0}),this}catch(e){throw Xe(1,e),e}}withExitCodeLogging(){try{return ve(ze,{logExitCode:!0}),this}catch(e){throw Xe(1,e),e}}withElementOnExit(){try{return ve(ze,{appendElementOnExit:!0}),this}catch(e){throw Xe(1,e),e}}withInteropCleanupOnExit(){try{return ve(ze,{interopCleanupOnExit:!0}),this}catch(e){throw Xe(1,e),e}}withDumpThreadsOnNonZeroExit(){try{return ve(ze,{dumpThreadsOnNonZeroExit:!0}),this}catch(e){throw Xe(1,e),e}}withWaitingForDebugger(e){try{return ve(ze,{waitForDebugger:e}),this}catch(e){throw Xe(1,e),e}}withInterpreterPgo(e,t){try{return ve(ze,{interpreterPgo:e,interpreterPgoSaveDelay:t}),ze.runtimeOptions?ze.runtimeOptions.push("--interp-pgo-recording"):ze.runtimeOptions=["--interp-pgo-recording"],this}catch(e){throw Xe(1,e),e}}withConfig(e){try{return ve(ze,e),this}catch(e){throw Xe(1,e),e}}withConfigSrc(e){try{return e&&"string"==typeof e||Be(!1,"must be file path or URL"),Ee(We,{configSrc:e}),this}catch(e){throw Xe(1,e),e}}withVirtualWorkingDirectory(e){try{return e&&"string"==typeof e||Be(!1,"must be directory path"),ve(ze,{virtualWorkingDirectory:e}),this}catch(e){throw Xe(1,e),e}}withEnvironmentVariable(e,t){try{const o={};return o[e]=t,ve(ze,{environmentVariables:o}),this}catch(e){throw Xe(1,e),e}}withEnvironmentVariables(e){try{return e&&"object"==typeof e||Be(!1,"must be dictionary object"),ve(ze,{environmentVariables:e}),this}catch(e){throw Xe(1,e),e}}withDiagnosticTracing(e){try{return"boolean"!=typeof e&&Be(!1,"must be boolean"),ve(ze,{diagnosticTracing:e}),this}catch(e){throw Xe(1,e),e}}withDebugging(e){try{return null!=e&&"number"==typeof e||Be(!1,"must be number"),ve(ze,{debugLevel:e}),this}catch(e){throw Xe(1,e),e}}withApplicationArguments(...e){try{return e&&Array.isArray(e)||Be(!1,"must be array of strings"),ve(ze,{applicationArguments:e}),this}catch(e){throw Xe(1,e),e}}withRuntimeOptions(e){try{return e&&Array.isArray(e)||Be(!1,"must be array of strings"),ze.runtimeOptions?ze.runtimeOptions.push(...e):ze.runtimeOptions=e,this}catch(e){throw Xe(1,e),e}}withMainAssembly(e){try{return ve(ze,{mainAssemblyName:e}),this}catch(e){throw Xe(1,e),e}}withApplicationArgumentsFromQuery(){try{if(!globalThis.window)throw new Error("Missing window to the query parameters from");if(void 0===globalThis.URLSearchParams)throw new Error("URLSearchParams is supported");const e=new URLSearchParams(globalThis.window.location.search).getAll("arg");return this.withApplicationArguments(...e)}catch(e){throw Xe(1,e),e}}withApplicationEnvironment(e){try{return ve(ze,{applicationEnvironment:e}),this}catch(e){throw Xe(1,e),e}}withApplicationCulture(e){try{return ve(ze,{applicationCulture:e}),this}catch(e){throw Xe(1,e),e}}withResourceLoader(e){try{return Pe.loadBootResource=e,this}catch(e){throw Xe(1,e),e}}async download(){try{await async function(){lt(We),await Re(We),re(),D(),oe(),await Pe.allDownloadsFinished.promise}()}catch(e){throw Xe(1,e),e}}async create(){try{return this.instance||(this.instance=await async function(){return await ct(We),Fe.api}()),this.instance}catch(e){throw Xe(1,e),e}}async run(){try{return We.config||Be(!1,"Null moduleConfig.config"),this.instance||await this.create(),this.instance.runMainAndExit()}catch(e){throw Xe(1,e),e}}},mt=Xe,gt=ct;Ie||"function"==typeof globalThis.URL||Be(!1,"This browser/engine doesn't support URL API. Please use a modern version. See also https://aka.ms/dotnet-wasm-features"),"function"!=typeof globalThis.BigInt64Array&&Be(!1,"This browser/engine doesn't support BigInt64Array API. Please use a modern version. See also https://aka.ms/dotnet-wasm-features"),ft.withConfig(/*json-start*/{
  "mainAssemblyName": "SimsRedux.Web",
  "resources": {
    "hash": "sha256-NT1pVJYATg/tmH8hPgQGTO0h2rAvO3TcIJ78v1hgFD8=",
    "jsModuleNative": [
      {
        "name": "dotnet.native.kzazliv0s3.js"
      }
    ],
    "jsModuleRuntime": [
      {
        "name": "dotnet.runtime.nkt61nz8g3.js"
      }
    ],
    "wasmNative": [
      {
        "name": "dotnet.native.bskgm3e8ek.wasm",
        "hash": "sha256-O2/SgMOzSU9pfF7NDUoe4iD5wkYECc3qlKCfvQkAhhU=",
        "cache": "force-cache"
      }
    ],
    "icu": [
      {
        "virtualPath": "icudt_CJK.dat",
        "name": "icudt_CJK.tjcz0u77k5.dat",
        "hash": "sha256-SZLtQnRc0JkwqHab0VUVP7T3uBPSeYzxzDnpxPpUnHk=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "icudt_EFIGS.dat",
        "name": "icudt_EFIGS.tptq2av103.dat",
        "hash": "sha256-8fItetYY8kQ0ww6oxwTLiT3oXlBwHKumbeP2pRF4yTc=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "icudt_no_CJK.dat",
        "name": "icudt_no_CJK.lfu7j35m59.dat",
        "hash": "sha256-L7sV7NEYP37/Qr2FPCePo5cJqRgTXRwGHuwF5Q+0Nfs=",
        "cache": "force-cache"
      }
    ],
    "coreAssembly": [
      {
        "virtualPath": "System.Private.CoreLib.wasm",
        "name": "System.Private.CoreLib.1dtcfdhvf4.wasm",
        "hash": "sha256-pbLsVvLhZjRlQE10fLm5x02zmAeqDyAbtFwlV/MyRQU=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.Runtime.InteropServices.JavaScript.wasm",
        "name": "System.Runtime.InteropServices.JavaScript.bp3e2ntofd.wasm",
        "hash": "sha256-P9hfhLfU3qI3VBaKyTedZMH9P6jiiO8rH/E9mSLtpJo=",
        "cache": "force-cache"
      }
    ],
    "assembly": [
      {
        "virtualPath": "Common.Logging.Core.wasm",
        "name": "Common.Logging.Core.mx7crur1qx.wasm",
        "hash": "sha256-KtaekQ8obBw9MaKylIwxJLw8XU3FBeZ+i5AQWyVnr9s=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "Common.Logging.wasm",
        "name": "Common.Logging.g46824d1id.wasm",
        "hash": "sha256-AE3EifKL+gGJzpFA9nxhqbPi8RzfLLgW25DFvpFTbWw=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "CommunityToolkit.HighPerformance.wasm",
        "name": "CommunityToolkit.HighPerformance.0crjkv741t.wasm",
        "hash": "sha256-IPHfNBGSkooZcY9p7f2hQWi5JKZalotxP5CTwS0MWgE=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "DeltaQ.BsDiff.wasm",
        "name": "DeltaQ.BsDiff.0jhejvfrhl.wasm",
        "hash": "sha256-PM/ymURuvst+YFO+WmOZc56kxUcP46w9nFa5RlIV7xA=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "DeltaQ.SuffixSorting.Abstractions.wasm",
        "name": "DeltaQ.SuffixSorting.Abstractions.u1tagy178d.wasm",
        "hash": "sha256-bqZFR+NaOVOLNcx5asRgZslWJR0iTpP/1OKgXDNNxEo=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "DeltaQ.SuffixSorting.LibDivSufSort.wasm",
        "name": "DeltaQ.SuffixSorting.LibDivSufSort.4wjnyxlm8g.wasm",
        "hash": "sha256-dBoVZNfy5t53z/M1JOzq4yNTlXHORCEmxniEgs1p5TI=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "DeltaQ.SuffixSorting.SAIS.wasm",
        "name": "DeltaQ.SuffixSorting.SAIS.rupjc7voo3.wasm",
        "hash": "sha256-Xf720/2JITzZY4w1epTpK+Wnpqk2x7kKwKJJExXZrr0=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "DeltaQ.Utility.Memory.wasm",
        "name": "DeltaQ.Utility.Memory.zoz2suv0hy.wasm",
        "hash": "sha256-M+X63TVQ8U8Th/U9gYdpOiQDN4YG2fptQ9ureCG1d9w=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "DeltaQ.wasm",
        "name": "DeltaQ.mmfrprck6h.wasm",
        "hash": "sha256-IrPgZlFXJz35ve6Ibl8ZXli2u1o/vlKc97mIiroGe4Q=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "GOLDEngine.wasm",
        "name": "GOLDEngine.5nt8var8gw.wasm",
        "hash": "sha256-7yBMXOqTqW2er6ALwikZJRS43tI9HNwVMHwpzNVr/1M=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "HandEvaluator.wasm",
        "name": "HandEvaluator.get2juktui.wasm",
        "hash": "sha256-WKy4tIVrwFcNBcXspzNtOTTnyR3As/bw2eQTNR5ecAQ=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "ICSharpCode.SharpZipLib.wasm",
        "name": "ICSharpCode.SharpZipLib.f8y4cl7s67.wasm",
        "hash": "sha256-sQnvcAtUeRrp/EWc52rziUXltWQhUBCAv/V77K+PsIg=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "Kni.Platform.wasm",
        "name": "Kni.Platform.f9kkav1lxw.wasm",
        "hash": "sha256-beY75p0ClqLeCbBVkHQCK0muJMc+Gby1alIPgo45mzg=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "MP3Sharp.wasm",
        "name": "MP3Sharp.wkprebq963.wasm",
        "hash": "sha256-wItmBdaYtkqdWPHtEbiAWZ8DQPK6ucLNiwMkyEsTbCY=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "MSDFData.wasm",
        "name": "MSDFData.01lqlqbqh3.wasm",
        "hash": "sha256-9PkGvl76RzFKtAKtg93f1qMjTQfqOvoqEmVmEHrupio=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "Mario.wasm",
        "name": "Mario.04xwhv741s.wasm",
        "hash": "sha256-oGyImDksfdgx8aiJBHj4y0GrCMqeHw8hTuh2Ij9+5v0=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "Microsoft.AspNetCore.Authorization.wasm",
        "name": "Microsoft.AspNetCore.Authorization.4w3z20tuqk.wasm",
        "hash": "sha256-jG9J70J7TtOrMBWM7jjeQAS39juTl4bJpRXGN6fhQlg=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "Microsoft.AspNetCore.Components.wasm",
        "name": "Microsoft.AspNetCore.Components.0z8r7oen0q.wasm",
        "hash": "sha256-LM04i1jy+LgVHqU5bGAbOGfq3sokEYpY33rzzuaadlw=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "Microsoft.AspNetCore.Components.Forms.wasm",
        "name": "Microsoft.AspNetCore.Components.Forms.y5xx1fx6r9.wasm",
        "hash": "sha256-JvMWXAOhDPo+zB8yHSWOEHEeT9/fcwZCQDwAxCPHXlg=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "Microsoft.AspNetCore.Components.Web.wasm",
        "name": "Microsoft.AspNetCore.Components.Web.aeyv0fw1i0.wasm",
        "hash": "sha256-HmTn6qpK4NWMdGwHU6HJ/nunn6y5o3uCm5V7+7M0480=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "Microsoft.AspNetCore.Components.WebAssembly.wasm",
        "name": "Microsoft.AspNetCore.Components.WebAssembly.0279vibpf3.wasm",
        "hash": "sha256-CH3a6skUDM+VD0EXPna4MFUS+MAICki7cC71hQWVQhQ=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "Microsoft.AspNetCore.Metadata.wasm",
        "name": "Microsoft.AspNetCore.Metadata.s8loo8n9nc.wasm",
        "hash": "sha256-dJZQlLCWV8HxVn+VLwiM3zh+2v2/BoHWNtaWvNqMdS8=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "Microsoft.CSharp.wasm",
        "name": "Microsoft.CSharp.u5ogspn8f9.wasm",
        "hash": "sha256-rhGtuFcM/rmp00MacvDKZXbd0b5EmnTjY5W+CsZEtC0=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "Microsoft.Extensions.Configuration.Abstractions.wasm",
        "name": "Microsoft.Extensions.Configuration.Abstractions.iqery9nry2.wasm",
        "hash": "sha256-Wtkv6KZJ6c4+tzKrdRIJ2f3EhIakYPDfAn2Rj04zHR0=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "Microsoft.Extensions.Configuration.Binder.wasm",
        "name": "Microsoft.Extensions.Configuration.Binder.7ebfchnp6m.wasm",
        "hash": "sha256-bwcA9NPW/7dv3k+Mr4N+gcZdNUYUtuUj3+O8YHwkbMc=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "Microsoft.Extensions.Configuration.FileExtensions.wasm",
        "name": "Microsoft.Extensions.Configuration.FileExtensions.pk5rwjur7z.wasm",
        "hash": "sha256-+UDJGDD1aV0uVsFf4YjM9MH7I+tb9CFJD1YfH0g3Wrc=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "Microsoft.Extensions.Configuration.Json.wasm",
        "name": "Microsoft.Extensions.Configuration.Json.m2qoahxjgt.wasm",
        "hash": "sha256-/pcKpSD6mBNjNREATQAkUv3Zf4H3LyuzGTLN4s679vs=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "Microsoft.Extensions.Configuration.wasm",
        "name": "Microsoft.Extensions.Configuration.qjwtydrbeh.wasm",
        "hash": "sha256-+o/D7fr2KXPZyZmjiCQLWPHVEh/oiumh8cIcUUlXN4U=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "Microsoft.Extensions.DependencyInjection.wasm",
        "name": "Microsoft.Extensions.DependencyInjection.0lh9y2h2sj.wasm",
        "hash": "sha256-RPtn6pkmjQgSJvC2r9Gf9YPVYplQr2s1x/1CuqngAo0=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "Microsoft.Extensions.DependencyInjection.Abstractions.wasm",
        "name": "Microsoft.Extensions.DependencyInjection.Abstractions.r4njuum1at.wasm",
        "hash": "sha256-27EFRDR3xbRa0nWlwVoIj7DmFf0h6tMjjYcNWEjPkNM=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "Microsoft.Extensions.Diagnostics.Abstractions.wasm",
        "name": "Microsoft.Extensions.Diagnostics.Abstractions.k7gpa90haf.wasm",
        "hash": "sha256-UGy6L0YpW2Jk6nCcbqyaTZ10KREZolKUCPnZP73JBSc=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "Microsoft.Extensions.Diagnostics.wasm",
        "name": "Microsoft.Extensions.Diagnostics.e4xkfi1ghr.wasm",
        "hash": "sha256-UhSR/+KrqvW8cRihNiuuZKZub5H7Bi6Uu5hn74ct0ok=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "Microsoft.Extensions.FileProviders.Abstractions.wasm",
        "name": "Microsoft.Extensions.FileProviders.Abstractions.pzlq0qczlw.wasm",
        "hash": "sha256-K+bL2FtukXzpFJCa3zncXjGqgfdxO9pnlf2wde9g3UQ=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "Microsoft.Extensions.FileProviders.Physical.wasm",
        "name": "Microsoft.Extensions.FileProviders.Physical.gp2mcne8f6.wasm",
        "hash": "sha256-NN9DfS/PNCg9pw+/XvOTZ7d3DxETLJ3DiLjZ2kOmL+0=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "Microsoft.Extensions.FileSystemGlobbing.wasm",
        "name": "Microsoft.Extensions.FileSystemGlobbing.bmq20vrvql.wasm",
        "hash": "sha256-Qz96yGM90nRLDI1XCXF6mre4CdDEiJLgnGOrszdMH2s=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "Microsoft.Extensions.Logging.Abstractions.wasm",
        "name": "Microsoft.Extensions.Logging.Abstractions.ef2lrhidbn.wasm",
        "hash": "sha256-ij5nuTCdqyB4gAlMGyLXxnmohZYnn3SC233Zqd40tYU=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "Microsoft.Extensions.Logging.wasm",
        "name": "Microsoft.Extensions.Logging.sceabnebwe.wasm",
        "hash": "sha256-1FHSWAgdUAeGtFFXIYqElWp8gy2mikQNMtTN8+/oo30=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "Microsoft.Extensions.Options.ConfigurationExtensions.wasm",
        "name": "Microsoft.Extensions.Options.ConfigurationExtensions.cbvevgdsho.wasm",
        "hash": "sha256-+No81T5HXezhQaZ4c1145qD3lvRa4r9Q3FWXw6ZfgAg=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "Microsoft.Extensions.Options.wasm",
        "name": "Microsoft.Extensions.Options.llivjk1whj.wasm",
        "hash": "sha256-6ULG5niYM+MHArol/TOSkMbfxTc7D5jKcorcHt/RPo0=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "Microsoft.Extensions.Primitives.wasm",
        "name": "Microsoft.Extensions.Primitives.ndur9bx6x9.wasm",
        "hash": "sha256-ixdbQi4Qj2Bd8xbpEM/764LZNO1roqKfS/hVzZiePNc=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "Microsoft.Extensions.Validation.wasm",
        "name": "Microsoft.Extensions.Validation.w1rf8w9vwy.wasm",
        "hash": "sha256-TPHp2vON5qjVdsb6ZuDh0rxu/FCFbB3r2IMcQHThpfo=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "Microsoft.JSInterop.WebAssembly.wasm",
        "name": "Microsoft.JSInterop.WebAssembly.4u1gumewbn.wasm",
        "hash": "sha256-tLy3qNrIC+jAb4i4SjHRuKzQlxxUQCDHYV6b4oUjEV4=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "Microsoft.JSInterop.wasm",
        "name": "Microsoft.JSInterop.r7i6scgm0q.wasm",
        "hash": "sha256-SZIr25HLqaeri9f5UuJpYtWWdLH4iSpFr5RmCLj3CAI=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "Microsoft.VisualBasic.wasm",
        "name": "Microsoft.VisualBasic.0w1vkzaqyz.wasm",
        "hash": "sha256-sMRdJvbGHF+Y8YJGrmP6zhTaV9ozEAq5p7FlpYRX9ik=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "Microsoft.VisualBasic.Core.wasm",
        "name": "Microsoft.VisualBasic.Core.wo0j4p5bwa.wasm",
        "hash": "sha256-wN2h7mrq5vLB+C95vtiVJrqfP7JO2sV0WBQvRzBV5Ak=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "Microsoft.Win32.Primitives.wasm",
        "name": "Microsoft.Win32.Primitives.10cgayguzg.wasm",
        "hash": "sha256-P8168OkVIoOXJdXFq7pVLdLNLnOKu6sxX3ZQLz8kOSA=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "Microsoft.Win32.Registry.wasm",
        "name": "Microsoft.Win32.Registry.013keampsq.wasm",
        "hash": "sha256-VCJjBr8J41MOlOivSwTdqD+9+NZ0QutZP7f7tSiqEZI=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "Mina.NET.wasm",
        "name": "Mina.NET.1crl29odc9.wasm",
        "hash": "sha256-oUO1uRta6mICInuCD/OLAUyc5Mm9VbmJn/vErahRCK0=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "Newtonsoft.Json.wasm",
        "name": "Newtonsoft.Json.jcjjiqe038.wasm",
        "hash": "sha256-s8KVuknfxWl1cuDvQM/OnpBfnpM1rxzvzq21S1cF36U=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "Ninject.wasm",
        "name": "Ninject.f1kkaeoefj.wasm",
        "hash": "sha256-c+TPYMpGVjIgm/GcG4G4VzF8BpYEES5L/chKavm0nNw=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "SimsRedux.Web.wasm",
        "name": "SimsRedux.Web.q32zb9db1e.wasm",
        "hash": "sha256-ZjQcasOzee8MAQ1D3Gg2uPymifZA+m+/AOeo1Nc/o1U=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "StbImageSharp.wasm",
        "name": "StbImageSharp.6tqf0ix22j.wasm",
        "hash": "sha256-hrKpDL+eBXHc///9vcW8fTssD6azKmMEpp/Yc0kJR3k=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.AppContext.wasm",
        "name": "System.AppContext.8snd0f2zhs.wasm",
        "hash": "sha256-qDV7Zrgu2B8VBVJKLUxz42BiLR4DFckcV/OZZpy26qU=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.Buffers.wasm",
        "name": "System.Buffers.y5drwcp192.wasm",
        "hash": "sha256-muRQYRi0uuO6qdFOMv6C8c8UInlNbonbGcmW+/PCXas=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.Collections.wasm",
        "name": "System.Collections.2g5gh5i3d6.wasm",
        "hash": "sha256-kkSjPzILQ4oQUjW9v9S/vdLZ7ZoOwmmPLVdQbrD4eh8=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.Collections.Concurrent.wasm",
        "name": "System.Collections.Concurrent.g5581b6ma4.wasm",
        "hash": "sha256-7KZdFWm36/mV8qA52L/1oqDxD6HuTnFCTKFXQ3qb0+Q=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.Collections.Immutable.wasm",
        "name": "System.Collections.Immutable.3mdyq0lowt.wasm",
        "hash": "sha256-ayNpVrDYCxRmz5G0hm7Yg8r0hE/teG+yI5yJd2tzlf8=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.Collections.NonGeneric.wasm",
        "name": "System.Collections.NonGeneric.nszwq1gk0d.wasm",
        "hash": "sha256-N3uOztssVf9V+AhC3Fmwv2riZAFmeeb8N6YZihR6cto=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.Collections.Specialized.wasm",
        "name": "System.Collections.Specialized.3i6m1fnd50.wasm",
        "hash": "sha256-a7ehNVJUxLFt/CcFGezcIGWCYeYmRB5DzS5UhM/lx9I=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.ComponentModel.Annotations.wasm",
        "name": "System.ComponentModel.Annotations.4eoxlk92cb.wasm",
        "hash": "sha256-vA5zt8wxjOhnDZPHfadkJRTb90L/0Jj6DdlKfXQUVgI=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.ComponentModel.DataAnnotations.wasm",
        "name": "System.ComponentModel.DataAnnotations.l9foahf45g.wasm",
        "hash": "sha256-t375nC6H2P+fL29ITcAPitvUEPgiXv4PJ9E+vlMGffI=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.ComponentModel.EventBasedAsync.wasm",
        "name": "System.ComponentModel.EventBasedAsync.feq6o2u27u.wasm",
        "hash": "sha256-2XRv1AJzjLzfWIXRxmtpCWBtovQ7Zv9OnpsoG2O2EHo=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.ComponentModel.Primitives.wasm",
        "name": "System.ComponentModel.Primitives.avlzof7ypc.wasm",
        "hash": "sha256-igIJXFN31rTtGGdGPjOEA52WSyPEl6mIGZOzjqFx4/M=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.ComponentModel.TypeConverter.wasm",
        "name": "System.ComponentModel.TypeConverter.ytujvt0xcn.wasm",
        "hash": "sha256-KorUwvL9ek30hqbWJjtxYOVNHmLOBJv83nXY4SYqfyQ=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.ComponentModel.wasm",
        "name": "System.ComponentModel.su8v3onrf8.wasm",
        "hash": "sha256-lLHccRbOGrwXI9YEeUT3c4aNdtL20u38KyGVjnyFspQ=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.Configuration.wasm",
        "name": "System.Configuration.cgk7espjr1.wasm",
        "hash": "sha256-tI6TDD+trvutRiUgmPfe1zyl1O6e2HVlHIjAuCUym/g=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.Console.wasm",
        "name": "System.Console.izn81gdzu1.wasm",
        "hash": "sha256-kmN9ASBwcHDz/NbfGxHK/DeDR7gKwLG7E55C3MA881c=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.Core.wasm",
        "name": "System.Core.cby8te2fuo.wasm",
        "hash": "sha256-SItrspFhNPHOcdL6b8AR/q95TxvPCG6/aDzUvymPCVI=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.Data.Common.wasm",
        "name": "System.Data.Common.rq3dsypzno.wasm",
        "hash": "sha256-MwjD38J04AxX51m8LfCN55Fn/yLHx3moqQ/LI2J+xug=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.Data.DataSetExtensions.wasm",
        "name": "System.Data.DataSetExtensions.gi2y7zsmbm.wasm",
        "hash": "sha256-OFRy/9dONgg9ldgWfwIFMbsv9ul5E7AnjkzDLH8b7XU=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.Data.wasm",
        "name": "System.Data.kw9gesg45b.wasm",
        "hash": "sha256-3KsoBaQ4WfHAzhT+L8vPImOBTL9U5HfTmd2sb8vrRzs=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.Diagnostics.Contracts.wasm",
        "name": "System.Diagnostics.Contracts.v2r7s8ysfb.wasm",
        "hash": "sha256-KZ+96VtHBmDIDZRS9wq+2Qhc2ScHyX8vWNwysBv1lEA=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.Diagnostics.Debug.wasm",
        "name": "System.Diagnostics.Debug.15vvgy239o.wasm",
        "hash": "sha256-ZWeMAhHMSmt+gxNA7eRxtrRz9PgqSUaApf2u6FHQiGY=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.Diagnostics.DiagnosticSource.wasm",
        "name": "System.Diagnostics.DiagnosticSource.orw0xlbec9.wasm",
        "hash": "sha256-5Ar7Kkjd4cg+3PhDAcP6kGM7c9qpq0kYcfUh+crItus=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.Diagnostics.FileVersionInfo.wasm",
        "name": "System.Diagnostics.FileVersionInfo.haj2gg9fz9.wasm",
        "hash": "sha256-15FltJ47VS7JqeC+6zAjB5N/EDyJyaoF1APncr9jvL0=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.Diagnostics.Process.wasm",
        "name": "System.Diagnostics.Process.rba90dc3ln.wasm",
        "hash": "sha256-eY9u0evSwLjNSrxab63Baren6raDF3vqG2EBFjA7/Uo=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.Diagnostics.StackTrace.wasm",
        "name": "System.Diagnostics.StackTrace.loenp7v39h.wasm",
        "hash": "sha256-FY/m3BSTidkawFfWp0/ir5stgcZENMQdwX0CssxAkFQ=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.Diagnostics.TextWriterTraceListener.wasm",
        "name": "System.Diagnostics.TextWriterTraceListener.dsmki4nyhm.wasm",
        "hash": "sha256-o30hA/gkhu2A8q7esHvSSRmZnmfnvNzIBppUmGBPttU=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.Diagnostics.Tools.wasm",
        "name": "System.Diagnostics.Tools.2eg410helx.wasm",
        "hash": "sha256-+lztgacuBw0DnVh/4vUI0/K90UcAw2aXSFkty/k5DQ8=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.Diagnostics.TraceSource.wasm",
        "name": "System.Diagnostics.TraceSource.7d3j89lapn.wasm",
        "hash": "sha256-yzP6DT/WOM58eRBLMayyL/0PVND+nE0VqAaExMH45rc=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.Diagnostics.Tracing.wasm",
        "name": "System.Diagnostics.Tracing.3hxvm885r2.wasm",
        "hash": "sha256-N3HCx8IB5RsvZD+f6iej6Rm2P5YxSR9vqZ9SppxPO2A=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.Drawing.Primitives.wasm",
        "name": "System.Drawing.Primitives.tfb09wjyxi.wasm",
        "hash": "sha256-F+0YBD038gz02gtLtUhdTJB5EnWgBKkZpSefg/YR1bU=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.Drawing.wasm",
        "name": "System.Drawing.aneyad90ut.wasm",
        "hash": "sha256-WlNT0FcTQgzbCdORz0sR7pjtV4twRv8OttnsWFfa00M=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.Dynamic.Runtime.wasm",
        "name": "System.Dynamic.Runtime.kf933zvsmu.wasm",
        "hash": "sha256-gG6sVXVNPta6hPXBzZXZz8j50wfPhBw7SqbrtgzJraQ=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.Formats.Asn1.wasm",
        "name": "System.Formats.Asn1.epl3ve901v.wasm",
        "hash": "sha256-XmhJr+WE4e7fyU0W89XdLsmjVL0rw0tmnYFMndbQFG4=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.Formats.Nrbf.wasm",
        "name": "System.Formats.Nrbf.iqyuaar93r.wasm",
        "hash": "sha256-plJZruI/e0Kzf+136OXpD6zPYy5icFJPwfXmOEFoDos=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.Formats.Tar.wasm",
        "name": "System.Formats.Tar.vhtn657rmj.wasm",
        "hash": "sha256-7eBDOq0+yoSQEkiOSDnTzqUTAJGqOrdEDUk2xfcWe/A=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.Globalization.Calendars.wasm",
        "name": "System.Globalization.Calendars.xork6x0a32.wasm",
        "hash": "sha256-z8zealNq0ZDHP+lj/PvHOMHEhRe1qgWCPeQOoSRfYss=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.Globalization.Extensions.wasm",
        "name": "System.Globalization.Extensions.67rxh6roo2.wasm",
        "hash": "sha256-8kA4zICX/WRrjUst2uAAo6ETBO/0dRBv4zJp7Ut2+3I=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.Globalization.wasm",
        "name": "System.Globalization.o3jz20ragi.wasm",
        "hash": "sha256-dL3pf0AwV8k8rAHF+UV7ydobAiiH2Hrw+Ss5cwTXXJo=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.IO.Compression.Brotli.wasm",
        "name": "System.IO.Compression.Brotli.n5mexy725k.wasm",
        "hash": "sha256-VR9TpE3DwLGNx5+SP2f3M9+H+pYtj2NTK1y0/fQsDxU=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.IO.Compression.FileSystem.wasm",
        "name": "System.IO.Compression.FileSystem.yrvpjbl6z1.wasm",
        "hash": "sha256-4sNog3MMl5GO1xkPU7FOie9SF/bUdYXKF1+tKD3bbjc=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.IO.Compression.ZipFile.wasm",
        "name": "System.IO.Compression.ZipFile.o6r22nkrrt.wasm",
        "hash": "sha256-IPSmToZzdHolFoV8YZjbmczUdPnZFl5Ct9o9+Ce5ovY=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.IO.Compression.wasm",
        "name": "System.IO.Compression.e6z7iwx8my.wasm",
        "hash": "sha256-1o94xPxMxIMkCN76u9h0TW1M3U21BrmJ0RIl/2ZTotw=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.IO.FileSystem.AccessControl.wasm",
        "name": "System.IO.FileSystem.AccessControl.t5xog3iunv.wasm",
        "hash": "sha256-4VlJbHAJve4eI0NGK3vUwkI/h5Dr8B0KMPQUl8K05vY=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.IO.FileSystem.DriveInfo.wasm",
        "name": "System.IO.FileSystem.DriveInfo.70m8okbqku.wasm",
        "hash": "sha256-ZzmGAGruNxR3fCzlyROScvLizrQkg+J5CujLNlatcQU=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.IO.FileSystem.Primitives.wasm",
        "name": "System.IO.FileSystem.Primitives.dvaub469k1.wasm",
        "hash": "sha256-ia0M4cGNomFhGgxqektFkVwPU7p35Qt7TDRP1/AIVqg=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.IO.FileSystem.Watcher.wasm",
        "name": "System.IO.FileSystem.Watcher.q0pg5ydpes.wasm",
        "hash": "sha256-ViCE+DfJdwTxhYDPMlMhlNVi88irI8K17ArptO+vxYA=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.IO.FileSystem.wasm",
        "name": "System.IO.FileSystem.z1inehktr2.wasm",
        "hash": "sha256-J+0ZmU/sakN3KMmPwDNZc5vIhN/UZ0xjk7T4igNBjwM=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.IO.IsolatedStorage.wasm",
        "name": "System.IO.IsolatedStorage.6pq4ienac7.wasm",
        "hash": "sha256-KprWu/kM2Qw7MuB8nM/Od2nHajTIJIvlcOWfx2zikbY=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.IO.MemoryMappedFiles.wasm",
        "name": "System.IO.MemoryMappedFiles.766snscpyr.wasm",
        "hash": "sha256-P3WSVyIvtAMCcXD/gTudOFakhR+jsFCvalOJPfQJn4Q=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.IO.Pipelines.wasm",
        "name": "System.IO.Pipelines.mcmy1usitz.wasm",
        "hash": "sha256-pr4ghx2yAc5X1atI+jnZwEBihY+IjftUjKEZXLqKnOI=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.IO.Pipes.AccessControl.wasm",
        "name": "System.IO.Pipes.AccessControl.89f9e5godo.wasm",
        "hash": "sha256-fLdWo99QinRKp0UJOIDB3lNdP8lXKlnKJRFEgcfbELk=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.IO.Pipes.wasm",
        "name": "System.IO.Pipes.b70ryqb15z.wasm",
        "hash": "sha256-xzO0ZA18QU5HxmtKO5Ba6ctpR+YUo3fDTcQFeewpIqQ=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.IO.UnmanagedMemoryStream.wasm",
        "name": "System.IO.UnmanagedMemoryStream.xyl28wyvqw.wasm",
        "hash": "sha256-R7x4GGHXp8u5giSBUAid+eJq14xMflL/xBXWO878WoU=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.IO.wasm",
        "name": "System.IO.w6ldxqbbw9.wasm",
        "hash": "sha256-iIWSQ0ZFkHYEp8QHJGNOQG+SKS8dam7h5Vv8crY36bY=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.Linq.AsyncEnumerable.wasm",
        "name": "System.Linq.AsyncEnumerable.rt0j2zdbty.wasm",
        "hash": "sha256-ETcliB9KK7ufOQF6LagwZNWnFGqWJVdPu6pGoyDyDHM=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.Linq.Expressions.wasm",
        "name": "System.Linq.Expressions.73jbyz0oqn.wasm",
        "hash": "sha256-YwOAnKfcOTpDUdEzjO2OIGalh8LNo2ZbUUYGzQ5Wduo=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.Linq.Parallel.wasm",
        "name": "System.Linq.Parallel.dnd78dwit2.wasm",
        "hash": "sha256-J+Obxura3fiABefbogGqtE6E4tLsT3jwCWwzSmMW6E4=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.Linq.Queryable.wasm",
        "name": "System.Linq.Queryable.va2x2bbqob.wasm",
        "hash": "sha256-5wdsHJxew0pclAg2cY0IKLeAtLqAwHVUx3rupwFbZro=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.Linq.wasm",
        "name": "System.Linq.al40jozqf2.wasm",
        "hash": "sha256-Phrvh4MqgX9nKOw6shBTnDEYhb9GK0MB0wAFTyVQTGs=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.Memory.wasm",
        "name": "System.Memory.6qxmjdx82f.wasm",
        "hash": "sha256-wlT99ebAPMPMPfhs1zZxdDYYEExikhW3obagEORS590=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.Net.Http.Json.wasm",
        "name": "System.Net.Http.Json.0x7x8qanw1.wasm",
        "hash": "sha256-rEnkQDhlVJq5Y3GaeLhXWR2s5comsyb039FKIeVzpwU=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.Net.Http.wasm",
        "name": "System.Net.Http.qcfg2egu44.wasm",
        "hash": "sha256-umskS/MRfVMMcV0S9yoYa1b81tLkJMj5YzuzY0uzA8s=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.Net.HttpListener.wasm",
        "name": "System.Net.HttpListener.l7rfvge5ji.wasm",
        "hash": "sha256-gRZFEVtgQA4jx1A7ZCckDN1Xf/r45Whrik+CriGLi1Q=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.Net.Mail.wasm",
        "name": "System.Net.Mail.ve9ap7zxk6.wasm",
        "hash": "sha256-2QzcG06SSOXcTYAta9eNMX/HGw4A/IeuCk7XGj6EWuk=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.Net.NameResolution.wasm",
        "name": "System.Net.NameResolution.2qbp7vo841.wasm",
        "hash": "sha256-msqShTfj7Xgejknun19+xlGdUER8WKIZ03/Q4vUVvms=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.Net.NetworkInformation.wasm",
        "name": "System.Net.NetworkInformation.0pwkam515c.wasm",
        "hash": "sha256-hNTHa7W1do1YIsPJfcxngtAwk60cnI7eTbSM4E4UHwA=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.Net.Ping.wasm",
        "name": "System.Net.Ping.j9lactcfmg.wasm",
        "hash": "sha256-JypwAmb9IFdGyfFCEXh0y+tu7Fv/7y6OLsCAPrVqfnY=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.Net.Primitives.wasm",
        "name": "System.Net.Primitives.0ykixok51j.wasm",
        "hash": "sha256-iNurrjy+H5kMyl8cRJQuN5MZWEs+IagZNorL68K2LQg=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.Net.Quic.wasm",
        "name": "System.Net.Quic.9jgnyoip8x.wasm",
        "hash": "sha256-iypp0iT99hfw6wmaItHORxRf4l04uTOT75dmiXGolgM=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.Net.Requests.wasm",
        "name": "System.Net.Requests.kbk2omnf3c.wasm",
        "hash": "sha256-oMxp9wS6+QflTAgAN3S68u8zElrQSLnnuOYm6dfRB8s=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.Net.Security.wasm",
        "name": "System.Net.Security.wkndehlhr0.wasm",
        "hash": "sha256-oPrO/Cg6j/Ye5orRXZdYNmXI8Gxi5xQ1v4oLsJ4qn0Y=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.Net.ServerSentEvents.wasm",
        "name": "System.Net.ServerSentEvents.vu6y366q7x.wasm",
        "hash": "sha256-N/IsjbY9RWhXQLxfQ3VAJ6r3gpOEPmYoO5wnZ2DisE8=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.Net.ServicePoint.wasm",
        "name": "System.Net.ServicePoint.fyclny1vpe.wasm",
        "hash": "sha256-KbzvnVN73tHvZWALVSfaLxhVpUbXgmpFU1P5w//wdY0=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.Net.Sockets.wasm",
        "name": "System.Net.Sockets.ide9qqkj9t.wasm",
        "hash": "sha256-+kxx+kWG4VDfSrch3IsGFnE7se1mdEWtTY9ARiHvtFY=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.Net.WebClient.wasm",
        "name": "System.Net.WebClient.p2po9qr0vh.wasm",
        "hash": "sha256-D2qpCKGg50vg0jYz77XvbhcamKkIOkeLwi8xHAmFH2Y=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.Net.WebHeaderCollection.wasm",
        "name": "System.Net.WebHeaderCollection.d5pyq118sk.wasm",
        "hash": "sha256-0cF6ujKwkCMjZCt16HNnjj1PS2Dr8mkKNN/m5RKPFKU=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.Net.WebProxy.wasm",
        "name": "System.Net.WebProxy.7y2oqcpexo.wasm",
        "hash": "sha256-EcFT6tpvdU2oY8pFF4kIblI+8Fi8CU1jSXVdMmGREX8=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.Net.WebSockets.Client.wasm",
        "name": "System.Net.WebSockets.Client.2xtg7tio8f.wasm",
        "hash": "sha256-iulSDW2cyC6lASe1ADO1dX9w5lFyp8PVnhvIgBoiQkc=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.Net.WebSockets.wasm",
        "name": "System.Net.WebSockets.r24y1pp341.wasm",
        "hash": "sha256-Iz6H1XR7TuFRxqx5XTW2Te7A9Q8zf4aj5l+OL698P/w=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.Net.wasm",
        "name": "System.Net.rzqae8ioox.wasm",
        "hash": "sha256-J7Ukt5mNVC161/gbA2QMyI9SXCTvnFFzQimPlOMjB1o=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.Numerics.Vectors.wasm",
        "name": "System.Numerics.Vectors.xjihqo8rgd.wasm",
        "hash": "sha256-LXLz6s+jq9kTr701h4Udh3BasjGG0nGNOPcwjuDqebg=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.Numerics.wasm",
        "name": "System.Numerics.fkt61zaz13.wasm",
        "hash": "sha256-0bd2P04mx3mHqtNWQST2Ya1w0JCcx0OMU4G0RunacWg=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.ObjectModel.wasm",
        "name": "System.ObjectModel.dtaj15m47r.wasm",
        "hash": "sha256-fw385IwgvtKeM70rCYYHffM+QaLDZtKCkTRNjn8E7cM=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.Private.DataContractSerialization.wasm",
        "name": "System.Private.DataContractSerialization.2ky1cfds0q.wasm",
        "hash": "sha256-zqLQ9Z7OG0iAIBG7BM+BN3PDcAOJpUM1ad4g30pEI/E=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.Private.Uri.wasm",
        "name": "System.Private.Uri.tb3stbo1a6.wasm",
        "hash": "sha256-J/dVtVzX9JnJlYTQCXgi77tZosjJskXFgIzyDhnSN7U=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.Private.Xml.Linq.wasm",
        "name": "System.Private.Xml.Linq.gbcdxdjl9h.wasm",
        "hash": "sha256-nAQo388gMeJRYI6fnaWrnLLrDKigSvOmCR2ugdEJNGQ=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.Private.Xml.wasm",
        "name": "System.Private.Xml.lwtv5gersy.wasm",
        "hash": "sha256-21F3V7t2piqGkxRN3W/qM6NwTvDctop2aMHVqAVFmMc=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.Reflection.DispatchProxy.wasm",
        "name": "System.Reflection.DispatchProxy.ifdl7k1v4o.wasm",
        "hash": "sha256-wsG7fnljmCydx5wFAKVAAA1wtDZ+RgdwYXFWa5c7Tr8=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.Reflection.Emit.wasm",
        "name": "System.Reflection.Emit.0qky76gdmh.wasm",
        "hash": "sha256-mONf3YTyTVCwIoedOaX+YI8otQfdSYj9u3CQsgUxK1U=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.Reflection.Emit.ILGeneration.wasm",
        "name": "System.Reflection.Emit.ILGeneration.iv7lus257w.wasm",
        "hash": "sha256-4raJXtwTdgjLEi3RoOVQlmMsq4VcHikTv7AOELtHpBU=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.Reflection.Emit.Lightweight.wasm",
        "name": "System.Reflection.Emit.Lightweight.jc84dvxrd2.wasm",
        "hash": "sha256-vSN/qPij8zfsGTPf6yGs5JS5iBGl8cC8Lc+Sv+P7yvU=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.Reflection.Extensions.wasm",
        "name": "System.Reflection.Extensions.f7w2tjtxqu.wasm",
        "hash": "sha256-dXdvJcN5U8jFea+5UMQ33JbNrqLaPd1ju858VTcEnr8=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.Reflection.Metadata.wasm",
        "name": "System.Reflection.Metadata.2vv4qfeb4g.wasm",
        "hash": "sha256-ThBBEtNvDdBx5sFdvNh85vPybZg5naioHkcBByyeyhY=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.Reflection.Primitives.wasm",
        "name": "System.Reflection.Primitives.iv3iqhwhyn.wasm",
        "hash": "sha256-HmKOXRp2xVACS+7XYMKhfA3GhkF1zgtNVF3HlvZryY4=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.Reflection.TypeExtensions.wasm",
        "name": "System.Reflection.TypeExtensions.oeqtqiys4q.wasm",
        "hash": "sha256-ENMV9KEGPSrrbct9Ysm9Zm9a/N5/1T9hT5T6PX36Wxo=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.Reflection.wasm",
        "name": "System.Reflection.ce7o6y7td2.wasm",
        "hash": "sha256-jIbPOIJcA3KCfscCrQXddwF73pg81GCYqaZ6LHRXXnw=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.Resources.Extensions.wasm",
        "name": "System.Resources.Extensions.apm9rujbf4.wasm",
        "hash": "sha256-0gEcuBMUxxKMrz6RkeuUsRt99XVRP0cAID6AsuzaLaI=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.Resources.Reader.wasm",
        "name": "System.Resources.Reader.t5y50lfuv9.wasm",
        "hash": "sha256-MSDkMyT8AAxrGeCYETzMvfDCHRmS+O/1YzuivafxcsM=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.Resources.ResourceManager.wasm",
        "name": "System.Resources.ResourceManager.8jkvga6j2v.wasm",
        "hash": "sha256-tBHSsAkcL3Jbc+bWgJ9QLQF6j3kJyoMC5qW0xInPCrs=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.Resources.Writer.wasm",
        "name": "System.Resources.Writer.oel61ia169.wasm",
        "hash": "sha256-4Lps85+f5Z9pFYQo0/smPEkiCR4KQadItjiQs/Bj3Lk=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.Runtime.CompilerServices.Unsafe.wasm",
        "name": "System.Runtime.CompilerServices.Unsafe.h6orm51uxe.wasm",
        "hash": "sha256-KU0wunmjvwMtZkC0QdTbgg19Fk/QtnDhvvb0FeyEqHg=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.Runtime.CompilerServices.VisualC.wasm",
        "name": "System.Runtime.CompilerServices.VisualC.sr5hzzngj5.wasm",
        "hash": "sha256-eBbXUT5DpylLjdQ56149DDe4ZGMFyL28sjsDuHUN1GI=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.Runtime.Extensions.wasm",
        "name": "System.Runtime.Extensions.afm9qfbqqk.wasm",
        "hash": "sha256-xv9SBp97Aig2r3kTGPI91LJrrY38Ixfj6AxyjmfRE1o=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.Runtime.Handles.wasm",
        "name": "System.Runtime.Handles.1wj8rwupm1.wasm",
        "hash": "sha256-sbGwZwxQkEsU8xDfEYrSDPLRIxMuD5phbZ1LYV8WHuI=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.Runtime.InteropServices.wasm",
        "name": "System.Runtime.InteropServices.1en4s8cdef.wasm",
        "hash": "sha256-aQvO0aaGlkZd2pp+ph5+4AYQ8NgJ7bdTcRyQPoqr0og=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.Runtime.InteropServices.RuntimeInformation.wasm",
        "name": "System.Runtime.InteropServices.RuntimeInformation.nj5yc370ls.wasm",
        "hash": "sha256-kzg6qN12oLllYwBpT2qRRCqBZRkfvuMSbMIYh0sUAgo=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.Runtime.Intrinsics.wasm",
        "name": "System.Runtime.Intrinsics.ujfn8hkojj.wasm",
        "hash": "sha256-hiXLQ+zEGkfkx3powW2btJlGNT7Shzr3WH4qxWf3o6g=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.Runtime.Loader.wasm",
        "name": "System.Runtime.Loader.qh8a7r724c.wasm",
        "hash": "sha256-ghOsMeWU0BaK1KNa167R0ILOUgnmkFrQlEG/iaJhxhQ=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.Runtime.Numerics.wasm",
        "name": "System.Runtime.Numerics.qze3dpgplh.wasm",
        "hash": "sha256-WiB41cT2CI7sFSzbQ6BbaQtZb4vgTyMq+F+RAnJj6/E=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.Runtime.Serialization.Formatters.wasm",
        "name": "System.Runtime.Serialization.Formatters.llsmufhddz.wasm",
        "hash": "sha256-SQRTWU9Hzq5zccKQSwOGoLTJ46tOj4L4vu2yA6kdAas=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.Runtime.Serialization.Json.wasm",
        "name": "System.Runtime.Serialization.Json.lg01mea3h5.wasm",
        "hash": "sha256-axnwhNpSflflTmaCznUDunSbmiA7i8uUz3tS/JQyWfo=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.Runtime.Serialization.Primitives.wasm",
        "name": "System.Runtime.Serialization.Primitives.dno4j2zx0q.wasm",
        "hash": "sha256-yQBtYCsVK4g2yO3CB2zq7b9WMEXYxlHdvlqLIyqWvJE=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.Runtime.Serialization.Xml.wasm",
        "name": "System.Runtime.Serialization.Xml.4kf85idj74.wasm",
        "hash": "sha256-xD3vPxfwLj4WTNx4UP/e+Ad5wSQew4qd8MQej7o79lo=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.Runtime.Serialization.wasm",
        "name": "System.Runtime.Serialization.kpnqw6iytg.wasm",
        "hash": "sha256-iJKH3DLjinIAt6PqGjtfrru2jfTAtnqoQagM2ZLULMA=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.Runtime.wasm",
        "name": "System.Runtime.ndg55fg84n.wasm",
        "hash": "sha256-1VzZzhsa6lTrdROQ2TvOWOKMHJ/Le4Qr22QII/q8QAE=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.Security.AccessControl.wasm",
        "name": "System.Security.AccessControl.nzqnube9vz.wasm",
        "hash": "sha256-nQ3LdbcMqvq1cczHuwbbet20pMx4H7toXgM/b2GxxUM=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.Security.Claims.wasm",
        "name": "System.Security.Claims.hrpbs00j8t.wasm",
        "hash": "sha256-00yPYBd7nQ+vwZV4EeHY905MCQmbXQMYTcLFph1iRZc=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.Security.Cryptography.Algorithms.wasm",
        "name": "System.Security.Cryptography.Algorithms.vwqtwze6ms.wasm",
        "hash": "sha256-geZjOgbBzL7ro1kYpLx7lS6yn33vNUI25MzHXdt47ek=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.Security.Cryptography.Cng.wasm",
        "name": "System.Security.Cryptography.Cng.j8plfs0uc3.wasm",
        "hash": "sha256-/a1CyMBixOe7kgb6h6tDL2OCmUBS+6TYfIKn0eIpR08=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.Security.Cryptography.Csp.wasm",
        "name": "System.Security.Cryptography.Csp.6ui4awd8q1.wasm",
        "hash": "sha256-oi10w6KN2FDtUpqcGbI3GY2SxWGMlOsVlNovx+ndkI8=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.Security.Cryptography.Encoding.wasm",
        "name": "System.Security.Cryptography.Encoding.xj4a081fxw.wasm",
        "hash": "sha256-jSVSKri0/e0OpM6h1CYh9vmiOO6+ONs5ezyqxd08R+g=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.Security.Cryptography.OpenSsl.wasm",
        "name": "System.Security.Cryptography.OpenSsl.5txu9rq37h.wasm",
        "hash": "sha256-qVuJaXq4hGVr5pApO7/7S5ov3WBGUkLDifp5PvSExkU=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.Security.Cryptography.Primitives.wasm",
        "name": "System.Security.Cryptography.Primitives.1ehcizkgbo.wasm",
        "hash": "sha256-9x6JxTdmelaPkxxqwbHvRVsrvzAQAEAGH9ZsGw2YgY4=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.Security.Cryptography.X509Certificates.wasm",
        "name": "System.Security.Cryptography.X509Certificates.9il9fobpwb.wasm",
        "hash": "sha256-32uDgLjQLhvzPyeouVcgL7wy65vmYLwmAlDKJPx5bEA=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.Security.Cryptography.wasm",
        "name": "System.Security.Cryptography.ij18wzk9je.wasm",
        "hash": "sha256-MjqJsWeokNeA6HM2XHN8jd8d5Uhh5WEdcalHFON9zFw=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.Security.Principal.wasm",
        "name": "System.Security.Principal.42ma3qvguj.wasm",
        "hash": "sha256-LAkK5PDn7KJoUx6AUHEDWD3ZPY6AdPRw1f4P2AswFr4=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.Security.Principal.Windows.wasm",
        "name": "System.Security.Principal.Windows.p27z4008y2.wasm",
        "hash": "sha256-b20kaOg/J6SWhK3GICRAZgNfQCkndiu8skdzbcwuG4U=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.Security.SecureString.wasm",
        "name": "System.Security.SecureString.2kom2qjr7p.wasm",
        "hash": "sha256-0v9Rr/dl685MAPLIzUZrP8GM8g+cajFJ7PVcmAq0Gdg=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.Security.wasm",
        "name": "System.Security.itxx8gut7g.wasm",
        "hash": "sha256-im4oEetGMNGRSuH7vRyxCdESrlGwnGSLkZZGZB0ps9Q=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.ServiceModel.Web.wasm",
        "name": "System.ServiceModel.Web.ljalaensqf.wasm",
        "hash": "sha256-odrThiutmIckSlOt8m5pQXPhDxYIdDe++lnfes7LKSM=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.ServiceProcess.wasm",
        "name": "System.ServiceProcess.d8c3rmqly1.wasm",
        "hash": "sha256-U6xGJNtwlhGnpxy3eJoscF5dVSFgajlnkmIMnjERlbU=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.Text.Encoding.CodePages.wasm",
        "name": "System.Text.Encoding.CodePages.6qnctuqdu9.wasm",
        "hash": "sha256-HljivpifWRMcbTR3/9PA5iHTwfAyGMWnFn8srDdygRs=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.Text.Encoding.Extensions.wasm",
        "name": "System.Text.Encoding.Extensions.hkihk76ezl.wasm",
        "hash": "sha256-v5cPR8ZP7QbmS041SATCfvgLnRuQY94LGM4NZbbP5kk=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.Text.Encoding.wasm",
        "name": "System.Text.Encoding.n3x5khcdb2.wasm",
        "hash": "sha256-LVF10lue2Luj5TLfu0TVZXiQiz02f8rc8DRyvFM4fzg=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.Text.Encodings.Web.wasm",
        "name": "System.Text.Encodings.Web.acgl4f6ydr.wasm",
        "hash": "sha256-BrpyUZuO2Z6ltCwjrrqaL2MrmpQMDyF/BI0CoMw3S5k=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.Text.Json.wasm",
        "name": "System.Text.Json.fd9zno69uy.wasm",
        "hash": "sha256-M5E2BCrX+cF2KjZHYpYMjqJ8d1rPKeiAbuqXJ8PYK04=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.Text.RegularExpressions.wasm",
        "name": "System.Text.RegularExpressions.zgqb4ut5g3.wasm",
        "hash": "sha256-wyb2Ba0QAQNFSSXR5W+tKTJWF+myTsUMZIpw92yzMRQ=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.Threading.wasm",
        "name": "System.Threading.1hbrmbgu6x.wasm",
        "hash": "sha256-Kyt9JWcIj++6WS6ylF8z4j6pdaLCLy3tVKwEmOEWCeQ=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.Threading.AccessControl.wasm",
        "name": "System.Threading.AccessControl.9vgsqn280o.wasm",
        "hash": "sha256-ZXqTJvXrzgRXDAAag/LshbYJ4j/QW+kyYgCak91ijcU=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.Threading.Channels.wasm",
        "name": "System.Threading.Channels.e5lskdj1k0.wasm",
        "hash": "sha256-7k0grgbSV+v1ZXZT23TOJ3KVsbZzUlv5pKODzMjdrRA=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.Threading.Overlapped.wasm",
        "name": "System.Threading.Overlapped.vwxq83pc37.wasm",
        "hash": "sha256-0U093MfkbBPKUhU3aIeuJU9JhQZ77xtyLHkaMh8Xqv4=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.Threading.Tasks.Dataflow.wasm",
        "name": "System.Threading.Tasks.Dataflow.00qwnzwiku.wasm",
        "hash": "sha256-oK737nSGWv1M2pchBKo5duPPcjr3QAZugEdxQv6SfHM=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.Threading.Tasks.Extensions.wasm",
        "name": "System.Threading.Tasks.Extensions.86tmuz40oo.wasm",
        "hash": "sha256-MMe5Tx444LIdYQezINSAkSAaaqO/uotwyCixRHZTS6Q=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.Threading.Tasks.Parallel.wasm",
        "name": "System.Threading.Tasks.Parallel.0e6xqtf3cp.wasm",
        "hash": "sha256-aMNM2vTfP/bSUml53hH2DhcF9SH0yfq7TdTt5Hiwupw=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.Threading.Tasks.wasm",
        "name": "System.Threading.Tasks.fcfkc68kwc.wasm",
        "hash": "sha256-UZFITqYur7P0vrXKCo/oAm4Dse0z68KYjGmCsmB/Yoc=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.Threading.Thread.wasm",
        "name": "System.Threading.Thread.wwh2dhrtis.wasm",
        "hash": "sha256-MMi5UEabR+0Y3ODLEw6MKSc6bjOag/J6Ir8wX/maPUw=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.Threading.ThreadPool.wasm",
        "name": "System.Threading.ThreadPool.3p6bng6p29.wasm",
        "hash": "sha256-WbIbkQXF5emPQ9fXE0513YD4R5QcZ0xmhSWXl6nYT90=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.Threading.Timer.wasm",
        "name": "System.Threading.Timer.4i9okipxyi.wasm",
        "hash": "sha256-HDKakDPmSW4KqdwUNs+Y25+BP3wTSd99BLnDVG2iWtA=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.Transactions.Local.wasm",
        "name": "System.Transactions.Local.j4lh0mijrb.wasm",
        "hash": "sha256-zYJkI1fymtyZaDJDw7sXFwFF7shbD8PQooZSYJ85mWI=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.Transactions.wasm",
        "name": "System.Transactions.w7o0t5jlhx.wasm",
        "hash": "sha256-nFvbGNWWG0QRLWyMg69vXwnmUDrrPx46epFDy4LcnoU=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.ValueTuple.wasm",
        "name": "System.ValueTuple.sw950m24zy.wasm",
        "hash": "sha256-lAZAQyFdr5T6NybK19GecysuA2xeSxOmh7ZsDeh4EI4=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.Web.wasm",
        "name": "System.Web.0i36ylqh8q.wasm",
        "hash": "sha256-yPLaYca5sym0u027FEZOoe5FKNndzQ7yJJea4aot4JM=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.Web.HttpUtility.wasm",
        "name": "System.Web.HttpUtility.bj3wrh2prp.wasm",
        "hash": "sha256-5xiBBJAcB50uhEiNMIc4T0XDEuZ+4HNl/7gGbrrVapY=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.Windows.wasm",
        "name": "System.Windows.kxwxs80d1h.wasm",
        "hash": "sha256-CN92m8BJtMCyiukRuBlC5Y8fDXOIRSPjSKexsw76J6k=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.Xml.Linq.wasm",
        "name": "System.Xml.Linq.ewfirtr0iq.wasm",
        "hash": "sha256-AqA5BXqafP2L+8AmZ/709aD9ZFk6tCGBwGj9tEyRiaA=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.Xml.ReaderWriter.wasm",
        "name": "System.Xml.ReaderWriter.e1n8ksv8wx.wasm",
        "hash": "sha256-oprJrEPIj/chdDpeLjld86zIBlGawIKaXGZ7vaGG9Rs=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.Xml.Serialization.wasm",
        "name": "System.Xml.Serialization.p07m4kvkkr.wasm",
        "hash": "sha256-CRGbETd31vJPjZ5X8JbLfkBQSsJZOHCPx7j4gHzqLuw=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.Xml.XDocument.wasm",
        "name": "System.Xml.XDocument.1dxwn9tcg8.wasm",
        "hash": "sha256-5XukITRcV8cJ+8MOPNk6u2xHRs+5ASH5wInA6gRuVJo=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.Xml.XPath.XDocument.wasm",
        "name": "System.Xml.XPath.XDocument.hkc3g5tly2.wasm",
        "hash": "sha256-YbYb6ReXoV9BWMi//w91jZmamKcKbJG76QJ89IyoWjE=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.Xml.XPath.wasm",
        "name": "System.Xml.XPath.tlw1efzi2l.wasm",
        "hash": "sha256-UXW9m6CCuYUNk+NkkNzeSDAEmsTgPfyLNt7xRmkrEt8=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.Xml.XmlDocument.wasm",
        "name": "System.Xml.XmlDocument.r7khe3tqod.wasm",
        "hash": "sha256-l4rWgnk5oZpcYyoGFmY9M3Z6vzBvCcbCuUuiXgpu5Bc=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.Xml.XmlSerializer.wasm",
        "name": "System.Xml.XmlSerializer.47hldxrdqm.wasm",
        "hash": "sha256-UIvN5w9cxh9OlB7lSdm63TELWJr0QOAKTM/sFNMm5Pc=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.Xml.wasm",
        "name": "System.Xml.jq01l0wffa.wasm",
        "hash": "sha256-+67ZkHMPJTZQpsDKmy/MXBRqs+yYXSzPD8MUPELFPn0=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "System.wasm",
        "name": "System.d2mloy49tr.wasm",
        "hash": "sha256-Cz26FOtCpd2UoEhHEec73HOyTLJp+TCAmqhTROOMgSY=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "WindowsBase.wasm",
        "name": "WindowsBase.37slryrpry.wasm",
        "hash": "sha256-/zQrI0u52SRroM8RlSq6nSVwy+tWod7/U81fYPpXO7k=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "Xna.Framework.Audio.wasm",
        "name": "Xna.Framework.Audio.mcrz0flyx5.wasm",
        "hash": "sha256-NuG+WTgnVbloOAP/kXXK2oAxhnEPudExO34Ze8QmoqQ=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "Xna.Framework.Content.wasm",
        "name": "Xna.Framework.Content.n9cs5yzw5k.wasm",
        "hash": "sha256-G0oqEm4evY0xA5tk232gxzx6b8ExRAI8ujTGFBqKAbw=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "Xna.Framework.Devices.wasm",
        "name": "Xna.Framework.Devices.1hzb9wkc0k.wasm",
        "hash": "sha256-VfHbNBELCfIBmn5ROBFPJODvZqA6m9sajnNip8eEad4=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "Xna.Framework.Game.wasm",
        "name": "Xna.Framework.Game.8qp7rlcis3.wasm",
        "hash": "sha256-ANNptfhjWAidyqLI0oY2u7/QNOThJAQi3wUrBlboWt4=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "Xna.Framework.Graphics.wasm",
        "name": "Xna.Framework.Graphics.6r0ltlti4e.wasm",
        "hash": "sha256-Evi2ZycnFnN0LFMkK5Su9sDWz3mjb0oaC6S2NDkDe8c=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "Xna.Framework.Input.wasm",
        "name": "Xna.Framework.Input.nf3dblyhq8.wasm",
        "hash": "sha256-oxGPQcenYfwDnRNM5UWFFUU1O47ihbyKaUODLAxzow0=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "Xna.Framework.Media.wasm",
        "name": "Xna.Framework.Media.16bagr2gcz.wasm",
        "hash": "sha256-9ywlWGUfyK2cip0yvInJaFz9TSj0RG6p4vCvUDJ9Zzw=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "Xna.Framework.Storage.wasm",
        "name": "Xna.Framework.Storage.8o2lh3q7qc.wasm",
        "hash": "sha256-yP3nKRKn5+wdsgcAiT6hPrs/6jaia3Z631mdQcXxdj8=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "Xna.Framework.XR.wasm",
        "name": "Xna.Framework.XR.6mz8wlewhr.wasm",
        "hash": "sha256-siX+Pp/4iaazDPJMRMYR9ACZtM8eNL6vrhDfjcDhpOc=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "Xna.Framework.wasm",
        "name": "Xna.Framework.l1bt3f55ih.wasm",
        "hash": "sha256-KezZGPnFdmtoR+ONtLZe2QoxdHpSuLGHVlv4tGTRtPo=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "mscorlib.wasm",
        "name": "mscorlib.cw5m8agm4m.wasm",
        "hash": "sha256-XLfdinTpfXIsvLj3Ynn4gE3/6hUkoVuM+aLCnwSBTCU=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "netstandard.wasm",
        "name": "netstandard.5a0vnluvqm.wasm",
        "hash": "sha256-02F9/D4q+A37ooBxxI5ev/uzLJRKLEZTcbEUwcfqLSY=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "nkast.Wasm.Audio.wasm",
        "name": "nkast.Wasm.Audio.zw37p0hjg9.wasm",
        "hash": "sha256-bWe3AIihyqHAr96dL3juHAPqSDx9BWttayz/Fb0gFTE=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "nkast.Wasm.Canvas.wasm",
        "name": "nkast.Wasm.Canvas.akmu0nxg87.wasm",
        "hash": "sha256-uplD8of7oUYayaRp/EPhD0fvU9M0dcjPqOTcs0BZKHM=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "nkast.Wasm.Dom.wasm",
        "name": "nkast.Wasm.Dom.bvhs7b6zcj.wasm",
        "hash": "sha256-d9yBUP9kNXQQ9vKF1S1ERhTTrK+0C0PHzj2/st7VHd4=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "nkast.Wasm.JSInterop.wasm",
        "name": "nkast.Wasm.JSInterop.51s5eg49ly.wasm",
        "hash": "sha256-Kate/316UrRat8vrhHnuzsYmvTUtAuV4/F9k4V19ELg=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "nkast.Wasm.XHR.wasm",
        "name": "nkast.Wasm.XHR.2fof52i80h.wasm",
        "hash": "sha256-XrZ1QAixE14YBWo9CkTQRhx+MUWZmfpd3eK4SMPp83g=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "nkast.Wasm.XR.wasm",
        "name": "nkast.Wasm.XR.hq1uqp25mj.wasm",
        "hash": "sha256-t91Ona4Igg6Ts2fAmvzz4Ks669/fvfZpJtPJSvQrYXE=",
        "cache": "force-cache"
      },
      {
        "virtualPath": "xxHashSharp.wasm",
        "name": "xxHashSharp.25txjg7dgi.wasm",
        "hash": "sha256-xvSzvBOV0ls4Fv/wkaIey18xCk1vS94wC7U7GPLFoLE=",
        "cache": "force-cache"
      }
    ]
  },
  "debugLevel": 0,
  "globalizationMode": "sharded",
  "extensions": {
    "blazor": {}
  },
  "runtimeConfig": {
    "runtimeOptions": {
      "configProperties": {
        "Microsoft.AspNetCore.Components.Routing.RegexConstraintSupport": false,
        "System.Diagnostics.Debugger.IsSupported": false,
        "System.Diagnostics.Metrics.Meter.IsSupported": false,
        "System.Diagnostics.Tracing.EventSource.IsSupported": false,
        "System.GC.Server": true,
        "System.Globalization.Invariant": false,
        "System.TimeZoneInfo.Invariant": true,
        "System.Linq.Enumerable.IsSizeOptimized": true,
        "System.Net.Http.EnableActivityPropagation": false,
        "System.Net.Http.WasmEnableStreamingResponse": true,
        "System.Net.SocketsHttpHandler.Http3Support": false,
        "System.Reflection.Metadata.MetadataUpdater.IsSupported": false,
        "System.Resources.UseSystemResourceKeys": true,
        "System.Runtime.Serialization.EnableUnsafeBinaryFormatterSerialization": false,
        "System.Text.Encoding.EnableUnsafeUTF7Encoding": false,
        "System.Text.Json.JsonSerializer.IsReflectionEnabledByDefault": true
      }
    }
  }
}/*json-end*/);export{gt as default,ft as dotnet,mt as exit};

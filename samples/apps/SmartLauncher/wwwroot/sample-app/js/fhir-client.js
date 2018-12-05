(function e(t,n,r){function s(o,u){if(!n[o]){if(!t[o]){var a=typeof require=="function"&&require;if(!u&&a)return a(o,!0);if(i)return i(o,!0);var f=new Error("Cannot find module '"+o+"'");throw f.code="MODULE_NOT_FOUND",f}var l=n[o]={exports:{}};t[o][0].call(l.exports,function(e){var n=t[o][1][e];return s(n?n:e)},l,l.exports,e,t,n,r)}return n[o].exports}var i=typeof require=="function"&&require;for(var o=0;o<r.length;o++)s(r[o]);return s})({1:[function(require,module,exports){
    (function webpackUniversalModuleDefinition(root, factory) {
        if(typeof exports === 'object' && typeof module === 'object')
            module.exports = factory();
        else if(typeof define === 'function' && define.amd)
            define([], factory);
        else if(typeof exports === 'object')
            exports["fhir"] = factory();
        else
            root["fhir"] = factory();
    })(this, function() {
    return /******/ (function(modules) { // webpackBootstrap
    /******/ 	// The module cache
    /******/ 	var installedModules = {};
    
    /******/ 	// The require function
    /******/ 	function __webpack_require__(moduleId) {
    
    /******/ 		// Check if module is in cache
    /******/ 		if(installedModules[moduleId])
    /******/ 			return installedModules[moduleId].exports;
    
    /******/ 		// Create a new module (and put it into the cache)
    /******/ 		var module = installedModules[moduleId] = {
    /******/ 			exports: {},
    /******/ 			id: moduleId,
    /******/ 			loaded: false
    /******/ 		};
    
    /******/ 		// Execute the module function
    /******/ 		modules[moduleId].call(module.exports, module, module.exports, __webpack_require__);
    
    /******/ 		// Flag the module as loaded
    /******/ 		module.loaded = true;
    
    /******/ 		// Return the exports of the module
    /******/ 		return module.exports;
    /******/ 	}
    
    
    /******/ 	// expose the modules object (__webpack_modules__)
    /******/ 	__webpack_require__.m = modules;
    
    /******/ 	// expose the module cache
    /******/ 	__webpack_require__.c = installedModules;
    
    /******/ 	// __webpack_public_path__
    /******/ 	__webpack_require__.p = "";
    
    /******/ 	// Load entry module and return exports
    /******/ 	return __webpack_require__(0);
    /******/ })
    /************************************************************************/
    /******/ ([
    /* 0 */
    /***/ function(module, exports, __webpack_require__) {
    
        (function() {
            var mkFhir = __webpack_require__(1);
            var jquery = window['_jQuery'] || window['jQuery'];
    
            var defer = function(){
                pr = jquery.Deferred();
                pr.promise = pr.promise();
                return pr;
            };
            var adapter = {
                defer: defer,
                http: function(args) {
                    var ret = jquery.Deferred();
                    var opts = {
                        type: args.method,
                        url: args.url,
                        headers: args.headers,
                        dataType: "json",
                        contentType: "application/json",
                        data: args.data || args.params,
                        withCredentials: args.credentials === 'include',
                    };
                    jquery.ajax(opts)
                        .done(function(data, status, xhr) {ret.resolve({data: data, status: status, headers: xhr.getResponseHeader, config: args});})
                        .fail(function(err) {ret.reject({error: err, data: err, config: args});});
                    return ret.promise();
                }
            };
    
            var fhir = function(config) {
                return mkFhir(config, adapter);
            };
            fhir.defer = defer;
            module.exports = fhir;
    
        }).call(this);
    
    
    /***/ },
    /* 1 */
    /***/ function(module, exports, __webpack_require__) {
    
        (function() {
            var utils = __webpack_require__(2);
            var M = __webpack_require__(5);
            var query = __webpack_require__(6);
            var auth = __webpack_require__(7);
            var transport = __webpack_require__(9);
            var errors = __webpack_require__(10);
            var config = __webpack_require__(11);
            var bundle = __webpack_require__(12);
            var pt = __webpack_require__(13);
            var refs = __webpack_require__(14);
            var url = __webpack_require__(15);
            var decorate = __webpack_require__(16);
    
            var cache = {};
    
    
            var fhir = function(cfg, adapter){
                var Middleware = M.Middleware;
                var $$Attr = M.$$Attr;
    
                var $$Method = function(m){ return $$Attr('method', m);};
                var $$Header = function(h,v) {return $$Attr('headers.' + h, v);};
    
                var $Errors = Middleware(errors);
                var Defaults = Middleware(config(cfg, adapter))
                        .and($Errors)
                        .and(auth.$Basic)
                        .and(auth.$Bearer)
                        .and(auth.$Credentials)
                        .and(transport.$JsonData)
                        .and($$Header('Accept', 'application/json'))
                        .and($$Header('Content-Type', 'application/json'));
    
                var GET = Defaults.and($$Method('GET'));
                var POST = Defaults.and($$Method('POST'));
                var PUT = Defaults.and($$Method('PUT'));
                var DELETE = Defaults.and($$Method('DELETE'));
    
                var http = transport.Http(cfg, adapter);
    
                var Path = url.Path;
                var BaseUrl = Path(cfg.baseUrl);
                var resourceTypePath = BaseUrl.slash(":type || :resource.resourceType");
                var searchPath = resourceTypePath;
                var resourceTypeHxPath = resourceTypePath.slash("_history");
                var resourcePath = resourceTypePath.slash(":id || :resource.id");
                var resourceHxPath = resourcePath.slash("_history");
                var vreadPath =  resourceHxPath.slash(":versionId || :resource.meta.versionId");
                var resourceVersionPath = resourceHxPath.slash(":versionId || :resource.meta.versionId");
    
                var ReturnHeader = $$Header('Prefer', 'return=representation');
    
                var $Paging = Middleware(query.$Paging);
    
                return decorate({
                    conformance: GET.and(BaseUrl.slash("metadata")).end(http),
                    document: POST.and(BaseUrl.slash("Document")).end(http),
                    profile:  GET.and(BaseUrl.slash("Profile").slash(":type")).end(http),
                    transaction: POST.and(BaseUrl).end(http),
                    history: GET.and(BaseUrl.slash("_history")).and($Paging).end(http),
                    typeHistory: GET.and(resourceTypeHxPath).and($Paging).end(http),
                    resourceHistory: GET.and(resourceHxPath).and($Paging).end(http),
                    read: GET.and(pt.$WithPatient).and(resourcePath).end(http),
                    vread: GET.and(vreadPath).end(http),
                    "delete": DELETE.and(resourcePath).and(ReturnHeader).end(http),
                    create: POST.and(resourceTypePath).and(ReturnHeader).end(http),
                    validate: POST.and(resourceTypePath.slash("_validate")).end(http),
                    search: GET.and(resourceTypePath).and(pt.$WithPatient).and(query.$SearchParams).and($Paging).end(http),
                    update: PUT.and(resourcePath).and(ReturnHeader).end(http),
                    nextPage: GET.and(bundle.$$BundleLinkUrl("next")).end(http),
                    prevPage: GET.and(bundle.$$BundleLinkUrl("prev")).end(http),
                    resolve: GET.and(refs.resolve).end(http)
                }, adapter);
    
            };
            module.exports = fhir;
        }).call(this);
    
    
    /***/ },
    /* 2 */
    /***/ function(module, exports, __webpack_require__) {
    
        (function() {
          var merge = __webpack_require__(3);
    
          var RTRIM = /^[\s\uFEFF\xA0]+|[\s\uFEFF\xA0]+$/g;
    
          var trim = function(text) {
            return text ? text.toString().replace(RTRIM, "")  : "";
          };
    
          exports.trim = trim;
    
          var addKey = function(acc, str) {
            var pair, val;
            if (!str) {
              return null;
            }
            pair = str.split("=").map(trim);
            val = pair[1].replace(/(^"|"$)/g, '');
            if (val) {
              acc[pair[0]] = val;
            }
            return acc;
          };
    
          var type = function(obj) {
            var classToType;
            if (obj == null && obj === undefined) {
              return String(obj);
            }
            classToType = {
              '[object Boolean]': 'boolean',
              '[object Number]': 'number',
              '[object String]': 'string',
              '[object Function]': 'function',
              '[object Array]': 'array',
              '[object Date]': 'date',
              '[object RegExp]': 'regexp',
              '[object Object]': 'object'
            };
            return classToType[Object.prototype.toString.call(obj)];
          };
    
          exports.type = type;
    
          var assertArray = function(a) {
            if (type(a) !== 'array') {
              throw 'not array';
            }
            return a;
          };
    
          exports.assertArray = assertArray;
    
          var assertObject = function(a) {
            if (type(a) !== 'object') {
              throw 'not object';
            }
            return a;
          };
    
          exports.assertObject = assertObject;
    
          var reduceMap = function(m, fn, acc) {
            var k, v;
            acc || (acc = []);
            assertObject(m);
            return ((function() {
              var results;
              results = [];
              for (k in m) {
                v = m[k];
                results.push([k, v]);
              }
              return results;
            })()).reduce(fn, acc);
          };
    
          exports.reduceMap = reduceMap;
    
          var identity = function(x) {return x;};
    
          exports.identity = identity;
    
          var argsArray = function() {
             return Array.prototype.slice.call(arguments)
          };
    
          exports.argsArray = argsArray;
    
          var mergeLists = function() {
            var reduce;
            reduce = function(merged, nextMap) {
              var k, ret, v;
              ret = merge(true, merged);
              for (k in nextMap) {
                v = nextMap[k];
                ret[k] = (ret[k] || []).concat(v);
              }
              return ret;
            };
            return argsArray.apply(null, arguments).reduce(reduce, {});
          };
    
          exports.mergeLists = mergeLists;
    
          var absoluteUrl = function(baseUrl, ref) {
            if (!ref.match(/https?:\/\/./)) {
              return baseUrl + "/" + ref;
            } else {
              return ref;
            }
          };
    
          exports.absoluteUrl = absoluteUrl;
    
          var relativeUrl = function(baseUrl, ref) {
            if (ref.slice(ref, baseUrl.length + 1) === baseUrl + "/") {
              return ref.slice(baseUrl.length + 1);
            } else {
              return ref;
            }
          };
    
          exports.relativeUrl = relativeUrl;
    
          exports.resourceIdToUrl = function(id, baseUrl, type) {
            baseUrl = baseUrl.replace(/\/$/, '');
            id = id.replace(/^\//, '');
            if (id.indexOf('/') < 0) {
              return baseUrl + "/" + type + "/" + id;
            } else if (id.indexOf(baseUrl) !== 0) {
              return baseUrl + "/" + id;
            } else {
              return id;
            }
          };
    
          var walk = function(inner, outer, data, context) {
            var keysToMap, remapped;
            switch (type(data)) {
              case 'array':
                return outer(data.map(function(item) {
                  return inner(item, [data, context]);
                }), context);
              case 'object':
                keysToMap = function(acc, arg) {
                  var k, v;
                  k = arg[0], v = arg[1];
                  acc[k] = inner(v, [data].concat(context));
                  return acc;
                };
                remapped = reduceMap(data, keysToMap, {});
                return outer(remapped, context);
              default:
                return outer(data, context);
            }
          };
    
          exports.walk = walk;
    
          var postwalk = function(f, data, context) {
            if (!data) {
              return function(data, context) {
                return postwalk(f, data, context);
              };
            } else {
              return walk(postwalk(f), f, data, context);
            }
          };
    
          exports.postwalk = postwalk;
    
        }).call(this);
    
    
    /***/ },
    /* 3 */
    /***/ function(module, exports, __webpack_require__) {
    
        /* WEBPACK VAR INJECTION */(function(module) {/*!
         * @name JavaScript/NodeJS Merge v1.1.3
         * @author yeikos
         * @repository https://github.com/yeikos/js.merge
    
         * Copyright 2014 yeikos - MIT license
         * https://raw.github.com/yeikos/js.merge/master/LICENSE
         */
    
        ;(function(isNode) {
    
            function merge() {
    
                var items = Array.prototype.slice.call(arguments),
                    result = items.shift(),
                    deep = (result === true),
                    size = items.length,
                    item, index, key;
    
                if (deep || typeOf(result) !== 'object')
    
                    result = {};
    
                for (index=0;index<size;++index)
    
                    if (typeOf(item = items[index]) === 'object')
    
                        for (key in item)
    
                            result[key] = deep ? clone(item[key]) : item[key];
    
                return result;
    
            }
    
            function clone(input) {
    
                var output = input,
                    type = typeOf(input),
                    index, size;
    
                if (type === 'array') {
    
                    output = [];
                    size = input.length;
    
                    for (index=0;index<size;++index)
    
                        output[index] = clone(input[index]);
    
                } else if (type === 'object') {
    
                    output = {};
    
                    for (index in input)
    
                        output[index] = clone(input[index]);
    
                }
    
                return output;
    
            }
    
            function typeOf(input) {
    
                return ({}).toString.call(input).match(/\s([\w]+)/)[1].toLowerCase();
    
            }
    
            if (isNode) {
    
                module.exports = merge;
    
            } else {
    
                window.merge = merge;
    
            }
    
        })(typeof module === 'object' && module && typeof module.exports === 'object' && module.exports);
        /* WEBPACK VAR INJECTION */}.call(exports, __webpack_require__(4)(module)))
    
    /***/ },
    /* 4 */
    /***/ function(module, exports) {
    
        module.exports = function(module) {
            if(!module.webpackPolyfill) {
                module.deprecate = function() {};
                module.paths = [];
                // module.parent = undefined by default
                module.children = [];
                module.webpackPolyfill = 1;
            }
            return module;
        }
    
    
    /***/ },
    /* 5 */
    /***/ function(module, exports, __webpack_require__) {
    
        (function() {
            var utils = __webpack_require__(2);
    
            var id = function(x){return x;};
            var constantly = function(x){return function(){return x;};};
    
            var mwComposition = function(mw1, mw2){
                return function(h){ return mw1(mw2(h)); };
            };
    
            var Middleware = function(mw){
                mw.and = function(nmw){
                    return Middleware(mwComposition(mw, nmw));
                };
                mw.end = function(h){
                    return mw(h);
                };
                return mw;
            };
    
            // generate wm from function
            exports.$$Simple = function(f){
                return function(h){
                    return function(args){
                        return h(f(args));
                    };
                };
            };
    
            var setAttr = function(args, attr, value){
                var path = attr.split('.');
                var obj = args;
                for(var i = 0; i < (path.length - 1); i++){
                    var k = path[i];
                    obj = args[k];
                    if(!obj){
                        obj = {};
                        args[k] = obj;
                    }
                }
                obj[path[path.length - 1]] = value;
                return args;
            };
    
            // generate wm from function
            exports.$$Attr = function(attr, fn){
                return Middleware(function(h){
                    return function(args) {
                        var value = null;
                        if(utils.type(fn) == 'function'){
                           value = fn(args);
                        } else {
                            value = fn;
                        }
                        if(value == null && value == undefined){
                            return h(args);
                        }else {
                            return h(setAttr(args, attr, value));
                        }
                    };
                });
            };
    
            var Attribute = function(attr, fn){
                return Middleware(function(h){
                    return function(args) {
                        args[attr] = fn(args);
                        return h(args);
                    };
                });
            };
    
            var Method = function(method){
                return Attribute('method', constantly(method));
            };
    
            exports.Middleware = Middleware;
            exports.Attribute = Attribute;
            exports.Method = Method;
    
        }).call(this);
    
    
    /***/ },
    /* 6 */
    /***/ function(module, exports, __webpack_require__) {
    
        (function() {
            var utils = __webpack_require__(2);
    
            var type = utils.type;
    
            var assertArray = utils.assertArray;
    
            var assertObject = utils.assertObject;
    
            var reduceMap = utils.reduceMap;
    
            var identity = utils.identity;
    
            var OPERATORS = {
                $gt: 'gt',
                $lt: 'lt',
                $lte: 'lte',
                $gte: 'gte'
            };
    
            var MODIFIERS = {
                $asc: ':asc',
                $desc: ':desc',
                $exact: ':exact',
                $missing: ':missing',
                $null: ':missing',
                $text: ':text'
            };
    
            var isOperator = function(v) {
                return v.indexOf('$') === 0;
            };
    
            var expandParam = function(k, v) {
                return reduceMap(v, function(acc, arg) {
                    var kk, o, res, vv;
                    kk = arg[0], vv = arg[1];
                    return acc.concat(kk === '$and' ? assertArray(vv).reduce((function(a, vvv) {
                        return a.concat(linearizeOne(k, vvv));
                    }), []) : kk === '$type' ? [] : isOperator(kk) ? (o = {
                        param: k
                    }, kk === '$or' ? o.value = vv : (OPERATORS[kk] ? o.operator = OPERATORS[kk] : void 0, MODIFIERS[kk] ? o.modifier = MODIFIERS[kk] : void 0, type(vv) === 'object' && vv.$or ? o.value = vv.$or : o.value = [vv]), [o]) : (v.$type ? res = ":" + v.$type : void 0, linearizeOne("" + k + (res || '') + "." + kk, vv)));
                });
            };
    
            var handleSort = function(xs) {
                var i, len, results, x;
                assertArray(xs);
                results = [];
                for (i = 0, len = xs.length; i < len; i++) {
                    x = xs[i];
                    switch (type(x)) {
                    case 'array':
                        results.push({
                            param: '_sort',
                            value: x[0],
                            modifier: ":" + x[1]
                        });
                        break;
                    case 'string':
                        results.push({
                            param: '_sort',
                            value: x
                        });
                        break;
                    default:
                        results.push(void 0);
                    }
                }
                return results;
            };
    
            var handleInclude = function(includes) {
                return reduceMap(includes, function(acc, arg) {
                    var k, v;
                    k = arg[0], v = arg[1];
                    return acc.concat((function() {
                        switch (type(v)) {
                        case 'array':
                            return v.map(function(x) {
                                return {
                                    param: '_include',
                                    value: k + "." + x
                                };
                            });
                        case 'string':
                            return [
                                {
                                    param: '_include',
                                    value: k + "." + v
                                }
                            ];
                        }
                    })());
                });
            };
    
            var linearizeOne = function(k, v) {
                if (k === '$sort') {
                    return handleSort(v);
                } else if (k === '$include') {
                    return handleInclude(v);
                } else {
                    switch (type(v)) {
                    case 'object':
                        return expandParam(k, v);
                    case 'string':
                        return [
                            {
                                param: k,
                                value: [v]
                            }
                        ];
                    case 'number':
                        return [
                            {
                                param: k,
                                value: [v]
                            }
                        ];
                    case 'array':
                        return [
                            {
                                param: k,
                                value: [v.join("|")]
                            }
                        ];
                    default:
                        throw "could not linearizeParams " + (type(v));
                    }
                }
            };
    
            var linearizeParams = function(query) {
                return reduceMap(query, function(acc, arg) {
                    var k, v;
                    k = arg[0], v = arg[1];
                    return acc.concat(linearizeOne(k, v));
                });
            };
    
            var buildSearchParams = function(query) {
                var p, ps;
                ps = (function() {
                    var i, len, ref, results;
                    ref = linearizeParams(query);
                    results = [];
                    for (i = 0, len = ref.length; i < len; i++) {
                        p = ref[i];
                        results.push([p.param, p.modifier, '=', p.operator, encodeURIComponent(p.value)].filter(identity).join(''));
                    }
                    return results;
                })();
                return ps.join("&");
            };
    
            exports._query = linearizeParams;
    
            exports.query = buildSearchParams;
    
            var mw = __webpack_require__(5);
    
            exports.$SearchParams = mw.$$Attr('url', function(args){
                var url = args.url;
                if(args.query){
                     var queryStr = buildSearchParams(args.query);
                     return url + "?" + queryStr;
                }
                return url;
            });
    
    
            exports.$Paging = function(h){
                return function(args){
                    var params = args.params || {};
                    if(args.since){params._since = args.since;}
                    if(args.count){params._count = args.count;}
                    args.params = params;
                    return h(args);
                };
            };
    
    
        }).call(this);
    
    
    /***/ },
    /* 7 */
    /***/ function(module, exports, __webpack_require__) {
    
        (function() {
            var mw = __webpack_require__(5);
    
            var btoa = __webpack_require__(8).btoa;
    
            exports.$Basic = mw.$$Attr('headers.Authorization', function(args){
                if(args.auth && args.auth.user && args.auth.pass){
                    return "Basic " + btoa(args.auth.user + ":" + args.auth.pass);
                }
            });
    
            exports.$Bearer = mw.$$Attr('headers.Authorization', function(args){
                if(args.auth && args.auth.bearer){
                    return "Bearer " + args.auth.bearer;
                }
            });
    
            var credentials;
            // this first middleware sets the credentials attribute to empty, so
            // adapters cannot use it directly, thus enforcing a valid value to be parsed in.
            exports.$Credentials = mw.Middleware(mw.$$Attr('credentials', function(args){
              // Assign value for later checking
              credentials = args.credentials
    
              // Needs to return non-null and not-undefined
              // in order for value to be (un)set
              return '';
            })).and(mw.$$Attr('credentials', function(args){
                // check credentials for valid options, valid for fetch
                if(['same-origin', 'include'].indexOf(credentials) > -1 ){
                    return credentials;
                }
            }));
    
        }).call(this);
    
    
    /***/ },
    /* 8 */
    /***/ function(module, exports, __webpack_require__) {
    
        ;(function () {
    
          var object =  true ? exports : this; // #8: web workers
          var chars = 'ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789+/=';
    
          function InvalidCharacterError(message) {
            this.message = message;
          }
          InvalidCharacterError.prototype = new Error;
          InvalidCharacterError.prototype.name = 'InvalidCharacterError';
    
          // encoder
          // [https://gist.github.com/999166] by [https://github.com/nignag]
          object.btoa || (
          object.btoa = function (input) {
            var str = String(input);
            for (
              // initialize result and counter
              var block, charCode, idx = 0, map = chars, output = '';
              // if the next str index does not exist:
              //   change the mapping table to "="
              //   check if d has no fractional digits
              str.charAt(idx | 0) || (map = '=', idx % 1);
              // "8 - idx % 1 * 8" generates the sequence 2, 4, 6, 8
              output += map.charAt(63 & block >> 8 - idx % 1 * 8)
            ) {
              charCode = str.charCodeAt(idx += 3/4);
              if (charCode > 0xFF) {
                throw new InvalidCharacterError("'btoa' failed: The string to be encoded contains characters outside of the Latin1 range.");
              }
              block = block << 8 | charCode;
            }
            return output;
          });
    
          // decoder
          // [https://gist.github.com/1020396] by [https://github.com/atk]
          object.atob || (
          object.atob = function (input) {
            var str = String(input).replace(/=+$/, '');
            if (str.length % 4 == 1) {
              throw new InvalidCharacterError("'atob' failed: The string to be decoded is not correctly encoded.");
            }
            for (
              // initialize result and counters
              var bc = 0, bs, buffer, idx = 0, output = '';
              // get next character
              buffer = str.charAt(idx++);
              // character found in table? initialize bit storage and add its ascii value;
              ~buffer && (bs = bc % 4 ? bs * 64 + buffer : buffer,
                // and if not first of each 4 characters,
                // convert the first 8 bits to one ascii character
                bc++ % 4) ? output += String.fromCharCode(255 & bs >> (-2 * bc & 6)) : 0
            ) {
              // try to find character in table (0-63, not found => -1)
              buffer = chars.indexOf(buffer);
            }
            return output;
          });
    
        }());
    
    
    /***/ },
    /* 9 */
    /***/ function(module, exports, __webpack_require__) {
    
        (function() {
            var utils = __webpack_require__(2);
    
            exports.Http = function(cfg, adapter){
                return function(args){
                    if(args.debug){
                        console.log("\nDEBUG (request):", args.method, args.url, args);
                    }
                    var promise = (args.http || adapter.http  || cfg.http)(args);
                    if (args.debug && promise && promise.then){
                        promise.then(function(x){ console.log("\nDEBUG: (responce)", x);});
                    }
                    return promise;
                };
            };
    
            var toJson = function(x){
                return (utils.type(x) == 'object') ? JSON.stringify(x) : x;
            };
    
            exports.$JsonData = function(h){
                return function(args){
                    var data = args.bundle || args.data || args.resource;
                    if(data){
                        args.data = toJson(data);
                    }
                    return h(args);
                };
            };
    
        }).call(this);
    
    
    /***/ },
    /* 10 */
    /***/ function(module, exports) {
    
        module.exports = function(h){
            return function(args){
                try{
                    return h(args);
                }catch(e){
                    if(args.debug){
                       console.log("\nDEBUG: (ERROR in middleware)");
                       console.log(e.message);
                       console.log(e.stack);
                    }
                    if(!args.defer) {
                        console.log("\nDEBUG: (ERROR in middleware)");
                        console.log(e.message);
                        console.log(e.stack);
                        throw new Error("I need adapter.defer");
                    }
                    var deff = args.defer();
                    deff.reject(e);
                    return deff.promise;
                }
            };
        };
    
    
    /***/ },
    /* 11 */
    /***/ function(module, exports) {
    
        (function() {
            var copyAttr = function(from, to, attr){
                var v =  from[attr];
                if(v && !to[attr]) {to[attr] = v;}
                return from;
            };
    
            module.exports = function(cfg, adapter){
                return function(h){
                    return function(args){
                        copyAttr(cfg, args, 'baseUrl');
                        copyAttr(cfg, args, 'cache');
                        copyAttr(cfg, args, 'auth');
                        copyAttr(cfg, args, 'patient');
                        copyAttr(cfg, args, 'debug');
                        copyAttr(adapter, args, 'defer');
                        copyAttr(adapter, args, 'http');
                        return h(args);
                    };
                };
            };
        }).call(this);
    
    
    /***/ },
    /* 12 */
    /***/ function(module, exports) {
    
        exports.$$BundleLinkUrl =  function(rel){
            return function(h) {
                return function(args){
                    var matched = function(x){return x.relation && x.relation === rel;};
                    var res =  args.bundle && (args.bundle.link || []).filter(matched)[0];
                    if(res && res.url){
                        args.url = res.url;
                        args.data = null;
                        return h(args);
                    }
                    else{
                        throw new Error("No " + rel + " link found in bundle");
                    }
                };
            };
        };
    
    
    /***/ },
    /* 13 */
    /***/ function(module, exports, __webpack_require__) {
    
        (function() {
            var mw = __webpack_require__(5);
    
            // List of resources with 'patient' or 'subject' properties (as of FHIR DSTU2 1.0.0)
            var targets = [
                "Account",
                "AllergyIntolerance",
                "BodySite",
                "CarePlan",
                "Claim",
                "ClinicalImpression",
                "Communication",
                "CommunicationRequest",
                "Composition",
                "Condition",
                "Contract",
                "DetectedIssue",
                "Device",
                "DeviceUseRequest",
                "DeviceUseStatement",
                "DiagnosticOrder",
                "DiagnosticReport",
                "DocumentManifest",
                "DocumentReference",
                "Encounter",
                "EnrollmentRequest",
                "EpisodeOfCare",
                "FamilyMemberHistory",
                "Flag",
                "Goal",
                "ImagingObjectSelection",
                "ImagingStudy",
                "Immunization",
                "ImmunizationRecommendation",
                "List",
                "Media",
                "MedicationAdministration",
                "MedicationDispense",
                "MedicationOrder",
                "MedicationStatement",
                "NutritionOrder",
                "Observation",
                "Order",
                "Procedure",
                "ProcedureRequest",
                "QuestionnaireResponse",
                "ReferralRequest",
                "RelatedPerson",
                "RiskAssessment",
                "Specimen",
                "SupplyDelivery",
                "SupplyRequest",
                "VisionPrescription"
            ];
    
            exports.$WithPatient = mw.$$Simple(function(args){
                var type = args.type;
                if (args.patient) {
                    if (type === "Patient") {
                        args.query = args.query || {};
                        args.query["_id"] = args.patient;
                        args["id"] = args.patient;
                    } else if (targets.indexOf(type) >= 0){
                        args.query = args.query || {};
                        args.query["patient"] = args.patient;
                    }
                }
                return args;
            });
        }).call(this);
    
    
    /***/ },
    /* 14 */
    /***/ function(module, exports, __webpack_require__) {
    
        (function() {
            var utils = __webpack_require__(2);
    
            var CONTAINED = /^#(.*)/;
            var resolveContained = function(ref, resource) {
                var cid = ref.match(CONTAINED)[1];
                var ret = (resource.contained || []).filter(function(r){
                    return (r.id || r._id) == cid;
                })[0];
                return (ret && {content: ret}) || null;
            };
    
            var sync = function(arg) {
                var cache = arg.cache;
                var reference = arg.reference;
                var bundle = arg.bundle;
                var ref = reference;
                if (!ref.reference) {return null;}
                if (ref.reference.match(CONTAINED)) {return resolveContained(ref.reference, arg.resource);}
                var abs = utils.absoluteUrl(arg.baseUrl, ref.reference);
                var bundled = ((bundle && bundle.entry) || []).filter( function(e){
                    return e.id === abs;
                })[0];
                return bundled || (cache != null ? cache[abs] : void 0) || null;
            };
    
            var resolve = function(h){
                return function(args) {
                    var cacheMatched = sync(args);
                    var ref = args.reference;
                    var def = args.defer();
                    if (cacheMatched) {
                        if(!args.defer){ throw new Error("I need promise constructor 'adapter.defer' in adapter"); }
                        def.resolve(cacheMatched);
                        return def.promise;
                    }
                    if (!ref) {
                        throw new Error("No reference found");
                    }
                    if (ref && ref.reference.match(CONTAINED)) {
                        throw new Error("Contained resource not found");
                    }
                    args.url = utils.absoluteUrl(args.baseUrl, ref.reference);
                    args.data = null;
                    return h(args);
                };
            };
    
            module.exports.sync = sync;
            module.exports.resolve = resolve;
    
        }).call(this);
    
    
    /***/ },
    /* 15 */
    /***/ function(module, exports, __webpack_require__) {
    
        (function() {
            var utils = __webpack_require__(2);
            var core = __webpack_require__(5);
    
            var id = function(x){return x;};
            var constantly = function(x){return function(){return x;};};
    
            var get_in = function(obj, path){
                return path.split('.').reduce(function(acc,x){
                    if(acc == null || acc == undefined) { return null; }
                    return acc[x];
                }, obj);
            };
    
            var evalPropsExpr = function(exp, args){
                var exps =  exp.split('||').map(function(x){return x.trim().substring(1);});
                for(var i = 0; i < exps.length; i++){
                    var res = get_in(args, exps[i]);
                    if(res){ return res; }
                }
                return null;
            };
    
            var evalExpr = function(exp, args){
                if (exp.indexOf(":") == 0){
                    return evalPropsExpr(exp, args);
                } else {
                    return exp;
                }
            };
    
            var buildPathPart = function(pth, args){
                var k = evalExpr(pth.trim(), args);
                if(k==null || k === undefined){ throw new Error("Parameter "+pth+" is required: " + JSON.stringify(args)); }
                return k;
            };
    
            // path chaining function
            // which return haldler wrapper: (h, cfg)->(args -> promise)
            // it's chainable Path("baseUrl").slash(":type").slash(":id").slash("_history")(id, {})({id: 5, type: 'Patient'})
            // and composable p0 = Path("baseUrl); p1 = p0.slash("path)
            var Path = function(tkn, chain){
                //Chainable
                var new_chain = function(args){
                    return ((chain && (chain(args) + "/")) || "") +  buildPathPart(tkn, args);
                };
                var ch = core.Attribute('url', new_chain);
                ch.slash = function(tkn){
                    return Path(tkn, new_chain);
                };
                return ch;
            };
    
            exports.Path = Path;
        }).call(this);
    
    
    /***/ },
    /* 16 */
    /***/ function(module, exports) {
    
        (function() {
            var fhirAPI;
            var adapter;
    
            function getNext (bundle, process) {
                var i;
                var d = bundle.data.entry || [];
                var entries = [];
                for (i = 0; i < d.length; i++) {
                    entries.push(d[i].resource);
                }
                process(entries);
                var def = adapter.defer();
                fhirAPI.nextPage({bundle:bundle.data}).then(function (r) {
                    getNext(r, process).then(function (t) {
                        def.resolve();
                    });
                }, function(err) {def.resolve()});
                return def.promise;
            }
            
            function drain (searchParams, process, done, fail) {
                var ret = adapter.defer();
                
                fhirAPI.search(searchParams).then(function(data){
                    getNext(data, process).then(function() {
                        done();
                    }, function(err) {
                        fail(err);
                    });
                }, function(err) {
                    fail(err);
                });
            };
            
            function fetchAll (searchParams){
                var ret = adapter.defer();
                var results = [];
                
                drain(
                    searchParams,
                    function(entries) {
                        entries.forEach(function(entry) {
                            results.push(entry);
                        });
                    },
                    function () {
                        ret.resolve(results);
                    },
                    function (err) {
                        ret.reject(err);
                    }
                );
                  
                return ret.promise;
            };
    
            function fetchAllWithReferences (searchParams, resolveParams) {
                var ret = adapter.defer();
                  
                fhirAPI.search(searchParams)  // TODO: THIS IS NOT CORRECT (need fetchAll, but it does not return a bundle yet)
                    .then(function(results){
    
                        var resolvedReferences = {};
    
                        var queue = [function() {
                            var entries = results.data.entry || [];
                            var res = entries.map(function(r){
                                return r.resource;
                            });
                            var refs = function (resource, reference) {
                                var refID = normalizeRefID(resource,reference);
                                return resolvedReferences[refID];
                            };
                            ret.resolve(res,refs);
                        }];
    
                        function normalizeRefID (resource, reference) {
                            var refID = reference.reference;
                            if (refID.startsWith('#')) {
                                var resourceID = resource.resourceType + "/" + resource.id;
                                return resourceID + refID;
                            } else {
                                return refID;
                            }
                        }
                        
                        function enqueue (bundle,resource,reference) {
                          queue.push(function() {
                            resolveReference(bundle,resource,reference);
                          });
                        }
    
                        function next() {
                          (queue.pop())();
                        }
    
                        function resolveReference (bundle,resource,reference) {
                            var refID = normalizeRefID(resource,reference);
                            fhirAPI.resolve({'bundle': bundle, 'resource': resource, 'reference':reference}).then(function(res){
                              var referencedObject = res.data || res.content;
                              resolvedReferences[refID] = referencedObject;
                              next();
                            });
                        }
    
                        var bundle = results.data;
    
                        bundle.entry && bundle.entry.forEach(function(element){
                          var resource = element.resource;
                          var type = resource.resourceType;
                          resolveParams && resolveParams.forEach(function(resolveParam){
                            var param = resolveParam.split('.');
                            var targetType = param[0];
                            var targetElement = param[1];
                            var reference = resource[targetElement];
                            if (type === targetType && reference) {
                              var referenceID = reference.reference;
                              if (!resolvedReferences[referenceID]) {
                                enqueue(bundle,resource,reference);
                              }
                            }
                          });
                        });
    
                        next();
    
                    }, function(){
                        ret.reject("Could not fetch search results");
                    });
                  
                return ret.promise;
            };
    
            function decorate (client, newAdapter) {
                fhirAPI = client;
                adapter = newAdapter;
                client["drain"] = drain;
                client["fetchAll"] = fetchAll;
                client["fetchAllWithReferences"] = fetchAllWithReferences;
                return client;
            }
            
            module.exports = decorate;
        }).call(this);
    
    /***/ }
    /******/ ])
    });
    ;
    },{}],2:[function(require,module,exports){
    
    },{}],3:[function(require,module,exports){
    /*!
     * The buffer module from node.js, for the browser.
     *
     * @author   Feross Aboukhadijeh <feross@feross.org> <http://feross.org>
     * @license  MIT
     */
    
    var base64 = require('base64-js')
    var ieee754 = require('ieee754')
    
    exports.Buffer = Buffer
    exports.SlowBuffer = Buffer
    exports.INSPECT_MAX_BYTES = 50
    Buffer.poolSize = 8192
    
    /**
     * If `TYPED_ARRAY_SUPPORT`:
     *   === true    Use Uint8Array implementation (fastest)
     *   === false   Use Object implementation (most compatible, even IE6)
     *
     * Browsers that support typed arrays are IE 10+, Firefox 4+, Chrome 7+, Safari 5.1+,
     * Opera 11.6+, iOS 4.2+.
     *
     * Note:
     *
     * - Implementation must support adding new properties to `Uint8Array` instances.
     *   Firefox 4-29 lacked support, fixed in Firefox 30+.
     *   See: https://bugzilla.mozilla.org/show_bug.cgi?id=695438.
     *
     *  - Chrome 9-10 is missing the `TypedArray.prototype.subarray` function.
     *
     *  - IE10 has a broken `TypedArray.prototype.subarray` function which returns arrays of
     *    incorrect length in some situations.
     *
     * We detect these buggy browsers and set `TYPED_ARRAY_SUPPORT` to `false` so they will
     * get the Object implementation, which is slower but will work correctly.
     */
    var TYPED_ARRAY_SUPPORT = (function () {
      try {
        var buf = new ArrayBuffer(0)
        var arr = new Uint8Array(buf)
        arr.foo = function () { return 42 }
        return 42 === arr.foo() && // typed array instances can be augmented
            typeof arr.subarray === 'function' && // chrome 9-10 lack `subarray`
            new Uint8Array(1).subarray(1, 1).byteLength === 0 // ie10 has broken `subarray`
      } catch (e) {
        return false
      }
    })()
    
    /**
     * Class: Buffer
     * =============
     *
     * The Buffer constructor returns instances of `Uint8Array` that are augmented
     * with function properties for all the node `Buffer` API functions. We use
     * `Uint8Array` so that square bracket notation works as expected -- it returns
     * a single octet.
     *
     * By augmenting the instances, we can avoid modifying the `Uint8Array`
     * prototype.
     */
    function Buffer (subject, encoding, noZero) {
      if (!(this instanceof Buffer))
        return new Buffer(subject, encoding, noZero)
    
      var type = typeof subject
    
      // Find the length
      var length
      if (type === 'number')
        length = subject > 0 ? subject >>> 0 : 0
      else if (type === 'string') {
        if (encoding === 'base64')
          subject = base64clean(subject)
        length = Buffer.byteLength(subject, encoding)
      } else if (type === 'object' && subject !== null) { // assume object is array-like
        if (subject.type === 'Buffer' && isArray(subject.data))
          subject = subject.data
        length = +subject.length > 0 ? Math.floor(+subject.length) : 0
      } else
        throw new Error('First argument needs to be a number, array or string.')
    
      var buf
      if (TYPED_ARRAY_SUPPORT) {
        // Preferred: Return an augmented `Uint8Array` instance for best performance
        buf = Buffer._augment(new Uint8Array(length))
      } else {
        // Fallback: Return THIS instance of Buffer (created by `new`)
        buf = this
        buf.length = length
        buf._isBuffer = true
      }
    
      var i
      if (TYPED_ARRAY_SUPPORT && typeof subject.byteLength === 'number') {
        // Speed optimization -- use set if we're copying from a typed array
        buf._set(subject)
      } else if (isArrayish(subject)) {
        // Treat array-ish objects as a byte array
        if (Buffer.isBuffer(subject)) {
          for (i = 0; i < length; i++)
            buf[i] = subject.readUInt8(i)
        } else {
          for (i = 0; i < length; i++)
            buf[i] = ((subject[i] % 256) + 256) % 256
        }
      } else if (type === 'string') {
        buf.write(subject, 0, encoding)
      } else if (type === 'number' && !TYPED_ARRAY_SUPPORT && !noZero) {
        for (i = 0; i < length; i++) {
          buf[i] = 0
        }
      }
    
      return buf
    }
    
    // STATIC METHODS
    // ==============
    
    Buffer.isEncoding = function (encoding) {
      switch (String(encoding).toLowerCase()) {
        case 'hex':
        case 'utf8':
        case 'utf-8':
        case 'ascii':
        case 'binary':
        case 'base64':
        case 'raw':
        case 'ucs2':
        case 'ucs-2':
        case 'utf16le':
        case 'utf-16le':
          return true
        default:
          return false
      }
    }
    
    Buffer.isBuffer = function (b) {
      return !!(b != null && b._isBuffer)
    }
    
    Buffer.byteLength = function (str, encoding) {
      var ret
      str = str.toString()
      switch (encoding || 'utf8') {
        case 'hex':
          ret = str.length / 2
          break
        case 'utf8':
        case 'utf-8':
          ret = utf8ToBytes(str).length
          break
        case 'ascii':
        case 'binary':
        case 'raw':
          ret = str.length
          break
        case 'base64':
          ret = base64ToBytes(str).length
          break
        case 'ucs2':
        case 'ucs-2':
        case 'utf16le':
        case 'utf-16le':
          ret = str.length * 2
          break
        default:
          throw new Error('Unknown encoding')
      }
      return ret
    }
    
    Buffer.concat = function (list, totalLength) {
      assert(isArray(list), 'Usage: Buffer.concat(list[, length])')
    
      if (list.length === 0) {
        return new Buffer(0)
      } else if (list.length === 1) {
        return list[0]
      }
    
      var i
      if (totalLength === undefined) {
        totalLength = 0
        for (i = 0; i < list.length; i++) {
          totalLength += list[i].length
        }
      }
    
      var buf = new Buffer(totalLength)
      var pos = 0
      for (i = 0; i < list.length; i++) {
        var item = list[i]
        item.copy(buf, pos)
        pos += item.length
      }
      return buf
    }
    
    Buffer.compare = function (a, b) {
      assert(Buffer.isBuffer(a) && Buffer.isBuffer(b), 'Arguments must be Buffers')
      var x = a.length
      var y = b.length
      for (var i = 0, len = Math.min(x, y); i < len && a[i] === b[i]; i++) {}
      if (i !== len) {
        x = a[i]
        y = b[i]
      }
      if (x < y) {
        return -1
      }
      if (y < x) {
        return 1
      }
      return 0
    }
    
    // BUFFER INSTANCE METHODS
    // =======================
    
    function hexWrite (buf, string, offset, length) {
      offset = Number(offset) || 0
      var remaining = buf.length - offset
      if (!length) {
        length = remaining
      } else {
        length = Number(length)
        if (length > remaining) {
          length = remaining
        }
      }
    
      // must be an even number of digits
      var strLen = string.length
      assert(strLen % 2 === 0, 'Invalid hex string')
    
      if (length > strLen / 2) {
        length = strLen / 2
      }
      for (var i = 0; i < length; i++) {
        var byte = parseInt(string.substr(i * 2, 2), 16)
        assert(!isNaN(byte), 'Invalid hex string')
        buf[offset + i] = byte
      }
      return i
    }
    
    function utf8Write (buf, string, offset, length) {
      var charsWritten = blitBuffer(utf8ToBytes(string), buf, offset, length)
      return charsWritten
    }
    
    function asciiWrite (buf, string, offset, length) {
      var charsWritten = blitBuffer(asciiToBytes(string), buf, offset, length)
      return charsWritten
    }
    
    function binaryWrite (buf, string, offset, length) {
      return asciiWrite(buf, string, offset, length)
    }
    
    function base64Write (buf, string, offset, length) {
      var charsWritten = blitBuffer(base64ToBytes(string), buf, offset, length)
      return charsWritten
    }
    
    function utf16leWrite (buf, string, offset, length) {
      var charsWritten = blitBuffer(utf16leToBytes(string), buf, offset, length)
      return charsWritten
    }
    
    Buffer.prototype.write = function (string, offset, length, encoding) {
      // Support both (string, offset, length, encoding)
      // and the legacy (string, encoding, offset, length)
      if (isFinite(offset)) {
        if (!isFinite(length)) {
          encoding = length
          length = undefined
        }
      } else {  // legacy
        var swap = encoding
        encoding = offset
        offset = length
        length = swap
      }
    
      offset = Number(offset) || 0
      var remaining = this.length - offset
      if (!length) {
        length = remaining
      } else {
        length = Number(length)
        if (length > remaining) {
          length = remaining
        }
      }
      encoding = String(encoding || 'utf8').toLowerCase()
    
      var ret
      switch (encoding) {
        case 'hex':
          ret = hexWrite(this, string, offset, length)
          break
        case 'utf8':
        case 'utf-8':
          ret = utf8Write(this, string, offset, length)
          break
        case 'ascii':
          ret = asciiWrite(this, string, offset, length)
          break
        case 'binary':
          ret = binaryWrite(this, string, offset, length)
          break
        case 'base64':
          ret = base64Write(this, string, offset, length)
          break
        case 'ucs2':
        case 'ucs-2':
        case 'utf16le':
        case 'utf-16le':
          ret = utf16leWrite(this, string, offset, length)
          break
        default:
          throw new Error('Unknown encoding')
      }
      return ret
    }
    
    Buffer.prototype.toString = function (encoding, start, end) {
      var self = this
    
      encoding = String(encoding || 'utf8').toLowerCase()
      start = Number(start) || 0
      end = (end === undefined) ? self.length : Number(end)
    
      // Fastpath empty strings
      if (end === start)
        return ''
    
      var ret
      switch (encoding) {
        case 'hex':
          ret = hexSlice(self, start, end)
          break
        case 'utf8':
        case 'utf-8':
          ret = utf8Slice(self, start, end)
          break
        case 'ascii':
          ret = asciiSlice(self, start, end)
          break
        case 'binary':
          ret = binarySlice(self, start, end)
          break
        case 'base64':
          ret = base64Slice(self, start, end)
          break
        case 'ucs2':
        case 'ucs-2':
        case 'utf16le':
        case 'utf-16le':
          ret = utf16leSlice(self, start, end)
          break
        default:
          throw new Error('Unknown encoding')
      }
      return ret
    }
    
    Buffer.prototype.toJSON = function () {
      return {
        type: 'Buffer',
        data: Array.prototype.slice.call(this._arr || this, 0)
      }
    }
    
    Buffer.prototype.equals = function (b) {
      assert(Buffer.isBuffer(b), 'Argument must be a Buffer')
      return Buffer.compare(this, b) === 0
    }
    
    Buffer.prototype.compare = function (b) {
      assert(Buffer.isBuffer(b), 'Argument must be a Buffer')
      return Buffer.compare(this, b)
    }
    
    // copy(targetBuffer, targetStart=0, sourceStart=0, sourceEnd=buffer.length)
    Buffer.prototype.copy = function (target, target_start, start, end) {
      var source = this
    
      if (!start) start = 0
      if (!end && end !== 0) end = this.length
      if (!target_start) target_start = 0
    
      // Copy 0 bytes; we're done
      if (end === start) return
      if (target.length === 0 || source.length === 0) return
    
      // Fatal error conditions
      assert(end >= start, 'sourceEnd < sourceStart')
      assert(target_start >= 0 && target_start < target.length,
          'targetStart out of bounds')
      assert(start >= 0 && start < source.length, 'sourceStart out of bounds')
      assert(end >= 0 && end <= source.length, 'sourceEnd out of bounds')
    
      // Are we oob?
      if (end > this.length)
        end = this.length
      if (target.length - target_start < end - start)
        end = target.length - target_start + start
    
      var len = end - start
    
      if (len < 100 || !TYPED_ARRAY_SUPPORT) {
        for (var i = 0; i < len; i++) {
          target[i + target_start] = this[i + start]
        }
      } else {
        target._set(this.subarray(start, start + len), target_start)
      }
    }
    
    function base64Slice (buf, start, end) {
      if (start === 0 && end === buf.length) {
        return base64.fromByteArray(buf)
      } else {
        return base64.fromByteArray(buf.slice(start, end))
      }
    }
    
    function utf8Slice (buf, start, end) {
      var res = ''
      var tmp = ''
      end = Math.min(buf.length, end)
    
      for (var i = start; i < end; i++) {
        if (buf[i] <= 0x7F) {
          res += decodeUtf8Char(tmp) + String.fromCharCode(buf[i])
          tmp = ''
        } else {
          tmp += '%' + buf[i].toString(16)
        }
      }
    
      return res + decodeUtf8Char(tmp)
    }
    
    function asciiSlice (buf, start, end) {
      var ret = ''
      end = Math.min(buf.length, end)
    
      for (var i = start; i < end; i++) {
        ret += String.fromCharCode(buf[i])
      }
      return ret
    }
    
    function binarySlice (buf, start, end) {
      return asciiSlice(buf, start, end)
    }
    
    function hexSlice (buf, start, end) {
      var len = buf.length
    
      if (!start || start < 0) start = 0
      if (!end || end < 0 || end > len) end = len
    
      var out = ''
      for (var i = start; i < end; i++) {
        out += toHex(buf[i])
      }
      return out
    }
    
    function utf16leSlice (buf, start, end) {
      var bytes = buf.slice(start, end)
      var res = ''
      for (var i = 0; i < bytes.length; i += 2) {
        res += String.fromCharCode(bytes[i] + bytes[i + 1] * 256)
      }
      return res
    }
    
    Buffer.prototype.slice = function (start, end) {
      var len = this.length
      start = ~~start
      end = end === undefined ? len : ~~end
    
      if (start < 0) {
        start += len;
        if (start < 0)
          start = 0
      } else if (start > len) {
        start = len
      }
    
      if (end < 0) {
        end += len
        if (end < 0)
          end = 0
      } else if (end > len) {
        end = len
      }
    
      if (end < start)
        end = start
    
      if (TYPED_ARRAY_SUPPORT) {
        return Buffer._augment(this.subarray(start, end))
      } else {
        var sliceLen = end - start
        var newBuf = new Buffer(sliceLen, undefined, true)
        for (var i = 0; i < sliceLen; i++) {
          newBuf[i] = this[i + start]
        }
        return newBuf
      }
    }
    
    // `get` will be removed in Node 0.13+
    Buffer.prototype.get = function (offset) {
      console.log('.get() is deprecated. Access using array indexes instead.')
      return this.readUInt8(offset)
    }
    
    // `set` will be removed in Node 0.13+
    Buffer.prototype.set = function (v, offset) {
      console.log('.set() is deprecated. Access using array indexes instead.')
      return this.writeUInt8(v, offset)
    }
    
    Buffer.prototype.readUInt8 = function (offset, noAssert) {
      if (!noAssert) {
        assert(offset !== undefined && offset !== null, 'missing offset')
        assert(offset < this.length, 'Trying to read beyond buffer length')
      }
    
      if (offset >= this.length)
        return
    
      return this[offset]
    }
    
    function readUInt16 (buf, offset, littleEndian, noAssert) {
      if (!noAssert) {
        assert(typeof littleEndian === 'boolean', 'missing or invalid endian')
        assert(offset !== undefined && offset !== null, 'missing offset')
        assert(offset + 1 < buf.length, 'Trying to read beyond buffer length')
      }
    
      var len = buf.length
      if (offset >= len)
        return
    
      var val
      if (littleEndian) {
        val = buf[offset]
        if (offset + 1 < len)
          val |= buf[offset + 1] << 8
      } else {
        val = buf[offset] << 8
        if (offset + 1 < len)
          val |= buf[offset + 1]
      }
      return val
    }
    
    Buffer.prototype.readUInt16LE = function (offset, noAssert) {
      return readUInt16(this, offset, true, noAssert)
    }
    
    Buffer.prototype.readUInt16BE = function (offset, noAssert) {
      return readUInt16(this, offset, false, noAssert)
    }
    
    function readUInt32 (buf, offset, littleEndian, noAssert) {
      if (!noAssert) {
        assert(typeof littleEndian === 'boolean', 'missing or invalid endian')
        assert(offset !== undefined && offset !== null, 'missing offset')
        assert(offset + 3 < buf.length, 'Trying to read beyond buffer length')
      }
    
      var len = buf.length
      if (offset >= len)
        return
    
      var val
      if (littleEndian) {
        if (offset + 2 < len)
          val = buf[offset + 2] << 16
        if (offset + 1 < len)
          val |= buf[offset + 1] << 8
        val |= buf[offset]
        if (offset + 3 < len)
          val = val + (buf[offset + 3] << 24 >>> 0)
      } else {
        if (offset + 1 < len)
          val = buf[offset + 1] << 16
        if (offset + 2 < len)
          val |= buf[offset + 2] << 8
        if (offset + 3 < len)
          val |= buf[offset + 3]
        val = val + (buf[offset] << 24 >>> 0)
      }
      return val
    }
    
    Buffer.prototype.readUInt32LE = function (offset, noAssert) {
      return readUInt32(this, offset, true, noAssert)
    }
    
    Buffer.prototype.readUInt32BE = function (offset, noAssert) {
      return readUInt32(this, offset, false, noAssert)
    }
    
    Buffer.prototype.readInt8 = function (offset, noAssert) {
      if (!noAssert) {
        assert(offset !== undefined && offset !== null,
            'missing offset')
        assert(offset < this.length, 'Trying to read beyond buffer length')
      }
    
      if (offset >= this.length)
        return
    
      var neg = this[offset] & 0x80
      if (neg)
        return (0xff - this[offset] + 1) * -1
      else
        return this[offset]
    }
    
    function readInt16 (buf, offset, littleEndian, noAssert) {
      if (!noAssert) {
        assert(typeof littleEndian === 'boolean', 'missing or invalid endian')
        assert(offset !== undefined && offset !== null, 'missing offset')
        assert(offset + 1 < buf.length, 'Trying to read beyond buffer length')
      }
    
      var len = buf.length
      if (offset >= len)
        return
    
      var val = readUInt16(buf, offset, littleEndian, true)
      var neg = val & 0x8000
      if (neg)
        return (0xffff - val + 1) * -1
      else
        return val
    }
    
    Buffer.prototype.readInt16LE = function (offset, noAssert) {
      return readInt16(this, offset, true, noAssert)
    }
    
    Buffer.prototype.readInt16BE = function (offset, noAssert) {
      return readInt16(this, offset, false, noAssert)
    }
    
    function readInt32 (buf, offset, littleEndian, noAssert) {
      if (!noAssert) {
        assert(typeof littleEndian === 'boolean', 'missing or invalid endian')
        assert(offset !== undefined && offset !== null, 'missing offset')
        assert(offset + 3 < buf.length, 'Trying to read beyond buffer length')
      }
    
      var len = buf.length
      if (offset >= len)
        return
    
      var val = readUInt32(buf, offset, littleEndian, true)
      var neg = val & 0x80000000
      if (neg)
        return (0xffffffff - val + 1) * -1
      else
        return val
    }
    
    Buffer.prototype.readInt32LE = function (offset, noAssert) {
      return readInt32(this, offset, true, noAssert)
    }
    
    Buffer.prototype.readInt32BE = function (offset, noAssert) {
      return readInt32(this, offset, false, noAssert)
    }
    
    function readFloat (buf, offset, littleEndian, noAssert) {
      if (!noAssert) {
        assert(typeof littleEndian === 'boolean', 'missing or invalid endian')
        assert(offset + 3 < buf.length, 'Trying to read beyond buffer length')
      }
    
      return ieee754.read(buf, offset, littleEndian, 23, 4)
    }
    
    Buffer.prototype.readFloatLE = function (offset, noAssert) {
      return readFloat(this, offset, true, noAssert)
    }
    
    Buffer.prototype.readFloatBE = function (offset, noAssert) {
      return readFloat(this, offset, false, noAssert)
    }
    
    function readDouble (buf, offset, littleEndian, noAssert) {
      if (!noAssert) {
        assert(typeof littleEndian === 'boolean', 'missing or invalid endian')
        assert(offset + 7 < buf.length, 'Trying to read beyond buffer length')
      }
    
      return ieee754.read(buf, offset, littleEndian, 52, 8)
    }
    
    Buffer.prototype.readDoubleLE = function (offset, noAssert) {
      return readDouble(this, offset, true, noAssert)
    }
    
    Buffer.prototype.readDoubleBE = function (offset, noAssert) {
      return readDouble(this, offset, false, noAssert)
    }
    
    Buffer.prototype.writeUInt8 = function (value, offset, noAssert) {
      if (!noAssert) {
        assert(value !== undefined && value !== null, 'missing value')
        assert(offset !== undefined && offset !== null, 'missing offset')
        assert(offset < this.length, 'trying to write beyond buffer length')
        verifuint(value, 0xff)
      }
    
      if (offset >= this.length) return
    
      this[offset] = value
      return offset + 1
    }
    
    function writeUInt16 (buf, value, offset, littleEndian, noAssert) {
      if (!noAssert) {
        assert(value !== undefined && value !== null, 'missing value')
        assert(typeof littleEndian === 'boolean', 'missing or invalid endian')
        assert(offset !== undefined && offset !== null, 'missing offset')
        assert(offset + 1 < buf.length, 'trying to write beyond buffer length')
        verifuint(value, 0xffff)
      }
    
      var len = buf.length
      if (offset >= len)
        return
    
      for (var i = 0, j = Math.min(len - offset, 2); i < j; i++) {
        buf[offset + i] =
            (value & (0xff << (8 * (littleEndian ? i : 1 - i)))) >>>
                (littleEndian ? i : 1 - i) * 8
      }
      return offset + 2
    }
    
    Buffer.prototype.writeUInt16LE = function (value, offset, noAssert) {
      return writeUInt16(this, value, offset, true, noAssert)
    }
    
    Buffer.prototype.writeUInt16BE = function (value, offset, noAssert) {
      return writeUInt16(this, value, offset, false, noAssert)
    }
    
    function writeUInt32 (buf, value, offset, littleEndian, noAssert) {
      if (!noAssert) {
        assert(value !== undefined && value !== null, 'missing value')
        assert(typeof littleEndian === 'boolean', 'missing or invalid endian')
        assert(offset !== undefined && offset !== null, 'missing offset')
        assert(offset + 3 < buf.length, 'trying to write beyond buffer length')
        verifuint(value, 0xffffffff)
      }
    
      var len = buf.length
      if (offset >= len)
        return
    
      for (var i = 0, j = Math.min(len - offset, 4); i < j; i++) {
        buf[offset + i] =
            (value >>> (littleEndian ? i : 3 - i) * 8) & 0xff
      }
      return offset + 4
    }
    
    Buffer.prototype.writeUInt32LE = function (value, offset, noAssert) {
      return writeUInt32(this, value, offset, true, noAssert)
    }
    
    Buffer.prototype.writeUInt32BE = function (value, offset, noAssert) {
      return writeUInt32(this, value, offset, false, noAssert)
    }
    
    Buffer.prototype.writeInt8 = function (value, offset, noAssert) {
      if (!noAssert) {
        assert(value !== undefined && value !== null, 'missing value')
        assert(offset !== undefined && offset !== null, 'missing offset')
        assert(offset < this.length, 'Trying to write beyond buffer length')
        verifsint(value, 0x7f, -0x80)
      }
    
      if (offset >= this.length)
        return
    
      if (value >= 0)
        this.writeUInt8(value, offset, noAssert)
      else
        this.writeUInt8(0xff + value + 1, offset, noAssert)
      return offset + 1
    }
    
    function writeInt16 (buf, value, offset, littleEndian, noAssert) {
      if (!noAssert) {
        assert(value !== undefined && value !== null, 'missing value')
        assert(typeof littleEndian === 'boolean', 'missing or invalid endian')
        assert(offset !== undefined && offset !== null, 'missing offset')
        assert(offset + 1 < buf.length, 'Trying to write beyond buffer length')
        verifsint(value, 0x7fff, -0x8000)
      }
    
      var len = buf.length
      if (offset >= len)
        return
    
      if (value >= 0)
        writeUInt16(buf, value, offset, littleEndian, noAssert)
      else
        writeUInt16(buf, 0xffff + value + 1, offset, littleEndian, noAssert)
      return offset + 2
    }
    
    Buffer.prototype.writeInt16LE = function (value, offset, noAssert) {
      return writeInt16(this, value, offset, true, noAssert)
    }
    
    Buffer.prototype.writeInt16BE = function (value, offset, noAssert) {
      return writeInt16(this, value, offset, false, noAssert)
    }
    
    function writeInt32 (buf, value, offset, littleEndian, noAssert) {
      if (!noAssert) {
        assert(value !== undefined && value !== null, 'missing value')
        assert(typeof littleEndian === 'boolean', 'missing or invalid endian')
        assert(offset !== undefined && offset !== null, 'missing offset')
        assert(offset + 3 < buf.length, 'Trying to write beyond buffer length')
        verifsint(value, 0x7fffffff, -0x80000000)
      }
    
      var len = buf.length
      if (offset >= len)
        return
    
      if (value >= 0)
        writeUInt32(buf, value, offset, littleEndian, noAssert)
      else
        writeUInt32(buf, 0xffffffff + value + 1, offset, littleEndian, noAssert)
      return offset + 4
    }
    
    Buffer.prototype.writeInt32LE = function (value, offset, noAssert) {
      return writeInt32(this, value, offset, true, noAssert)
    }
    
    Buffer.prototype.writeInt32BE = function (value, offset, noAssert) {
      return writeInt32(this, value, offset, false, noAssert)
    }
    
    function writeFloat (buf, value, offset, littleEndian, noAssert) {
      if (!noAssert) {
        assert(value !== undefined && value !== null, 'missing value')
        assert(typeof littleEndian === 'boolean', 'missing or invalid endian')
        assert(offset !== undefined && offset !== null, 'missing offset')
        assert(offset + 3 < buf.length, 'Trying to write beyond buffer length')
        verifIEEE754(value, 3.4028234663852886e+38, -3.4028234663852886e+38)
      }
    
      var len = buf.length
      if (offset >= len)
        return
    
      ieee754.write(buf, value, offset, littleEndian, 23, 4)
      return offset + 4
    }
    
    Buffer.prototype.writeFloatLE = function (value, offset, noAssert) {
      return writeFloat(this, value, offset, true, noAssert)
    }
    
    Buffer.prototype.writeFloatBE = function (value, offset, noAssert) {
      return writeFloat(this, value, offset, false, noAssert)
    }
    
    function writeDouble (buf, value, offset, littleEndian, noAssert) {
      if (!noAssert) {
        assert(value !== undefined && value !== null, 'missing value')
        assert(typeof littleEndian === 'boolean', 'missing or invalid endian')
        assert(offset !== undefined && offset !== null, 'missing offset')
        assert(offset + 7 < buf.length,
            'Trying to write beyond buffer length')
        verifIEEE754(value, 1.7976931348623157E+308, -1.7976931348623157E+308)
      }
    
      var len = buf.length
      if (offset >= len)
        return
    
      ieee754.write(buf, value, offset, littleEndian, 52, 8)
      return offset + 8
    }
    
    Buffer.prototype.writeDoubleLE = function (value, offset, noAssert) {
      return writeDouble(this, value, offset, true, noAssert)
    }
    
    Buffer.prototype.writeDoubleBE = function (value, offset, noAssert) {
      return writeDouble(this, value, offset, false, noAssert)
    }
    
    // fill(value, start=0, end=buffer.length)
    Buffer.prototype.fill = function (value, start, end) {
      if (!value) value = 0
      if (!start) start = 0
      if (!end) end = this.length
    
      assert(end >= start, 'end < start')
    
      // Fill 0 bytes; we're done
      if (end === start) return
      if (this.length === 0) return
    
      assert(start >= 0 && start < this.length, 'start out of bounds')
      assert(end >= 0 && end <= this.length, 'end out of bounds')
    
      var i
      if (typeof value === 'number') {
        for (i = start; i < end; i++) {
          this[i] = value
        }
      } else {
        var bytes = utf8ToBytes(value.toString())
        var len = bytes.length
        for (i = start; i < end; i++) {
          this[i] = bytes[i % len]
        }
      }
    
      return this
    }
    
    Buffer.prototype.inspect = function () {
      var out = []
      var len = this.length
      for (var i = 0; i < len; i++) {
        out[i] = toHex(this[i])
        if (i === exports.INSPECT_MAX_BYTES) {
          out[i + 1] = '...'
          break
        }
      }
      return '<Buffer ' + out.join(' ') + '>'
    }
    
    /**
     * Creates a new `ArrayBuffer` with the *copied* memory of the buffer instance.
     * Added in Node 0.12. Only available in browsers that support ArrayBuffer.
     */
    Buffer.prototype.toArrayBuffer = function () {
      if (typeof Uint8Array !== 'undefined') {
        if (TYPED_ARRAY_SUPPORT) {
          return (new Buffer(this)).buffer
        } else {
          var buf = new Uint8Array(this.length)
          for (var i = 0, len = buf.length; i < len; i += 1) {
            buf[i] = this[i]
          }
          return buf.buffer
        }
      } else {
        throw new Error('Buffer.toArrayBuffer not supported in this browser')
      }
    }
    
    // HELPER FUNCTIONS
    // ================
    
    var BP = Buffer.prototype
    
    /**
     * Augment a Uint8Array *instance* (not the Uint8Array class!) with Buffer methods
     */
    Buffer._augment = function (arr) {
      arr._isBuffer = true
    
      // save reference to original Uint8Array get/set methods before overwriting
      arr._get = arr.get
      arr._set = arr.set
    
      // deprecated, will be removed in node 0.13+
      arr.get = BP.get
      arr.set = BP.set
    
      arr.write = BP.write
      arr.toString = BP.toString
      arr.toLocaleString = BP.toString
      arr.toJSON = BP.toJSON
      arr.equals = BP.equals
      arr.compare = BP.compare
      arr.copy = BP.copy
      arr.slice = BP.slice
      arr.readUInt8 = BP.readUInt8
      arr.readUInt16LE = BP.readUInt16LE
      arr.readUInt16BE = BP.readUInt16BE
      arr.readUInt32LE = BP.readUInt32LE
      arr.readUInt32BE = BP.readUInt32BE
      arr.readInt8 = BP.readInt8
      arr.readInt16LE = BP.readInt16LE
      arr.readInt16BE = BP.readInt16BE
      arr.readInt32LE = BP.readInt32LE
      arr.readInt32BE = BP.readInt32BE
      arr.readFloatLE = BP.readFloatLE
      arr.readFloatBE = BP.readFloatBE
      arr.readDoubleLE = BP.readDoubleLE
      arr.readDoubleBE = BP.readDoubleBE
      arr.writeUInt8 = BP.writeUInt8
      arr.writeUInt16LE = BP.writeUInt16LE
      arr.writeUInt16BE = BP.writeUInt16BE
      arr.writeUInt32LE = BP.writeUInt32LE
      arr.writeUInt32BE = BP.writeUInt32BE
      arr.writeInt8 = BP.writeInt8
      arr.writeInt16LE = BP.writeInt16LE
      arr.writeInt16BE = BP.writeInt16BE
      arr.writeInt32LE = BP.writeInt32LE
      arr.writeInt32BE = BP.writeInt32BE
      arr.writeFloatLE = BP.writeFloatLE
      arr.writeFloatBE = BP.writeFloatBE
      arr.writeDoubleLE = BP.writeDoubleLE
      arr.writeDoubleBE = BP.writeDoubleBE
      arr.fill = BP.fill
      arr.inspect = BP.inspect
      arr.toArrayBuffer = BP.toArrayBuffer
    
      return arr
    }
    
    var INVALID_BASE64_RE = /[^+\/0-9A-z]/g
    
    function base64clean (str) {
      // Node strips out invalid characters like \n and \t from the string, base64-js does not
      str = stringtrim(str).replace(INVALID_BASE64_RE, '')
      // Node allows for non-padded base64 strings (missing trailing ===), base64-js does not
      while (str.length % 4 !== 0) {
        str = str + '='
      }
      return str
    }
    
    function stringtrim (str) {
      if (str.trim) return str.trim()
      return str.replace(/^\s+|\s+$/g, '')
    }
    
    function isArray (subject) {
      return (Array.isArray || function (subject) {
        return Object.prototype.toString.call(subject) === '[object Array]'
      })(subject)
    }
    
    function isArrayish (subject) {
      return isArray(subject) || Buffer.isBuffer(subject) ||
          subject && typeof subject === 'object' &&
          typeof subject.length === 'number'
    }
    
    function toHex (n) {
      if (n < 16) return '0' + n.toString(16)
      return n.toString(16)
    }
    
    function utf8ToBytes (str) {
      var byteArray = []
      for (var i = 0; i < str.length; i++) {
        var b = str.charCodeAt(i)
        if (b <= 0x7F) {
          byteArray.push(b)
        } else {
          var start = i
          if (b >= 0xD800 && b <= 0xDFFF) i++
          var h = encodeURIComponent(str.slice(start, i+1)).substr(1).split('%')
          for (var j = 0; j < h.length; j++) {
            byteArray.push(parseInt(h[j], 16))
          }
        }
      }
      return byteArray
    }
    
    function asciiToBytes (str) {
      var byteArray = []
      for (var i = 0; i < str.length; i++) {
        // Node's code seems to be doing this and not & 0x7F..
        byteArray.push(str.charCodeAt(i) & 0xFF)
      }
      return byteArray
    }
    
    function utf16leToBytes (str) {
      var c, hi, lo
      var byteArray = []
      for (var i = 0; i < str.length; i++) {
        c = str.charCodeAt(i)
        hi = c >> 8
        lo = c % 256
        byteArray.push(lo)
        byteArray.push(hi)
      }
    
      return byteArray
    }
    
    function base64ToBytes (str) {
      return base64.toByteArray(str)
    }
    
    function blitBuffer (src, dst, offset, length) {
      for (var i = 0; i < length; i++) {
        if ((i + offset >= dst.length) || (i >= src.length))
          break
        dst[i + offset] = src[i]
      }
      return i
    }
    
    function decodeUtf8Char (str) {
      try {
        return decodeURIComponent(str)
      } catch (err) {
        return String.fromCharCode(0xFFFD) // UTF 8 invalid char
      }
    }
    
    /*
     * We have to make sure that the value is a valid integer. This means that it
     * is non-negative. It has no fractional component and that it does not
     * exceed the maximum allowed value.
     */
    function verifuint (value, max) {
      assert(typeof value === 'number', 'cannot write a non-number as a number')
      assert(value >= 0, 'specified a negative value for writing an unsigned value')
      assert(value <= max, 'value is larger than maximum value for type')
      assert(Math.floor(value) === value, 'value has a fractional component')
    }
    
    function verifsint (value, max, min) {
      assert(typeof value === 'number', 'cannot write a non-number as a number')
      assert(value <= max, 'value larger than maximum allowed value')
      assert(value >= min, 'value smaller than minimum allowed value')
      assert(Math.floor(value) === value, 'value has a fractional component')
    }
    
    function verifIEEE754 (value, max, min) {
      assert(typeof value === 'number', 'cannot write a non-number as a number')
      assert(value <= max, 'value larger than maximum allowed value')
      assert(value >= min, 'value smaller than minimum allowed value')
    }
    
    function assert (test, message) {
      if (!test) throw new Error(message || 'Failed assertion')
    }
    
    },{"base64-js":4,"ieee754":5}],4:[function(require,module,exports){
    var lookup = 'ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789+/';
    
    ;(function (exports) {
        'use strict';
    
      var Arr = (typeof Uint8Array !== 'undefined')
        ? Uint8Array
        : Array
    
        var PLUS   = '+'.charCodeAt(0)
        var SLASH  = '/'.charCodeAt(0)
        var NUMBER = '0'.charCodeAt(0)
        var LOWER  = 'a'.charCodeAt(0)
        var UPPER  = 'A'.charCodeAt(0)
    
        function decode (elt) {
            var code = elt.charCodeAt(0)
            if (code === PLUS)
                return 62 // '+'
            if (code === SLASH)
                return 63 // '/'
            if (code < NUMBER)
                return -1 //no match
            if (code < NUMBER + 10)
                return code - NUMBER + 26 + 26
            if (code < UPPER + 26)
                return code - UPPER
            if (code < LOWER + 26)
                return code - LOWER + 26
        }
    
        function b64ToByteArray (b64) {
            var i, j, l, tmp, placeHolders, arr
    
            if (b64.length % 4 > 0) {
                throw new Error('Invalid string. Length must be a multiple of 4')
            }
    
            // the number of equal signs (place holders)
            // if there are two placeholders, than the two characters before it
            // represent one byte
            // if there is only one, then the three characters before it represent 2 bytes
            // this is just a cheap hack to not do indexOf twice
            var len = b64.length
            placeHolders = '=' === b64.charAt(len - 2) ? 2 : '=' === b64.charAt(len - 1) ? 1 : 0
    
            // base64 is 4/3 + up to two characters of the original data
            arr = new Arr(b64.length * 3 / 4 - placeHolders)
    
            // if there are placeholders, only get up to the last complete 4 chars
            l = placeHolders > 0 ? b64.length - 4 : b64.length
    
            var L = 0
    
            function push (v) {
                arr[L++] = v
            }
    
            for (i = 0, j = 0; i < l; i += 4, j += 3) {
                tmp = (decode(b64.charAt(i)) << 18) | (decode(b64.charAt(i + 1)) << 12) | (decode(b64.charAt(i + 2)) << 6) | decode(b64.charAt(i + 3))
                push((tmp & 0xFF0000) >> 16)
                push((tmp & 0xFF00) >> 8)
                push(tmp & 0xFF)
            }
    
            if (placeHolders === 2) {
                tmp = (decode(b64.charAt(i)) << 2) | (decode(b64.charAt(i + 1)) >> 4)
                push(tmp & 0xFF)
            } else if (placeHolders === 1) {
                tmp = (decode(b64.charAt(i)) << 10) | (decode(b64.charAt(i + 1)) << 4) | (decode(b64.charAt(i + 2)) >> 2)
                push((tmp >> 8) & 0xFF)
                push(tmp & 0xFF)
            }
    
            return arr
        }
    
        function uint8ToBase64 (uint8) {
            var i,
                extraBytes = uint8.length % 3, // if we have 1 byte left, pad 2 bytes
                output = "",
                temp, length
    
            function encode (num) {
                return lookup.charAt(num)
            }
    
            function tripletToBase64 (num) {
                return encode(num >> 18 & 0x3F) + encode(num >> 12 & 0x3F) + encode(num >> 6 & 0x3F) + encode(num & 0x3F)
            }
    
            // go through the array every three bytes, we'll deal with trailing stuff later
            for (i = 0, length = uint8.length - extraBytes; i < length; i += 3) {
                temp = (uint8[i] << 16) + (uint8[i + 1] << 8) + (uint8[i + 2])
                output += tripletToBase64(temp)
            }
    
            // pad the end with zeros, but make sure to not forget the extra bytes
            switch (extraBytes) {
                case 1:
                    temp = uint8[uint8.length - 1]
                    output += encode(temp >> 2)
                    output += encode((temp << 4) & 0x3F)
                    output += '=='
                    break
                case 2:
                    temp = (uint8[uint8.length - 2] << 8) + (uint8[uint8.length - 1])
                    output += encode(temp >> 10)
                    output += encode((temp >> 4) & 0x3F)
                    output += encode((temp << 2) & 0x3F)
                    output += '='
                    break
            }
    
            return output
        }
    
        exports.toByteArray = b64ToByteArray
        exports.fromByteArray = uint8ToBase64
    }(typeof exports === 'undefined' ? (this.base64js = {}) : exports))
    
    },{}],5:[function(require,module,exports){
    exports.read = function(buffer, offset, isLE, mLen, nBytes) {
      var e, m,
          eLen = nBytes * 8 - mLen - 1,
          eMax = (1 << eLen) - 1,
          eBias = eMax >> 1,
          nBits = -7,
          i = isLE ? (nBytes - 1) : 0,
          d = isLE ? -1 : 1,
          s = buffer[offset + i];
    
      i += d;
    
      e = s & ((1 << (-nBits)) - 1);
      s >>= (-nBits);
      nBits += eLen;
      for (; nBits > 0; e = e * 256 + buffer[offset + i], i += d, nBits -= 8);
    
      m = e & ((1 << (-nBits)) - 1);
      e >>= (-nBits);
      nBits += mLen;
      for (; nBits > 0; m = m * 256 + buffer[offset + i], i += d, nBits -= 8);
    
      if (e === 0) {
        e = 1 - eBias;
      } else if (e === eMax) {
        return m ? NaN : ((s ? -1 : 1) * Infinity);
      } else {
        m = m + Math.pow(2, mLen);
        e = e - eBias;
      }
      return (s ? -1 : 1) * m * Math.pow(2, e - mLen);
    };
    
    exports.write = function(buffer, value, offset, isLE, mLen, nBytes) {
      var e, m, c,
          eLen = nBytes * 8 - mLen - 1,
          eMax = (1 << eLen) - 1,
          eBias = eMax >> 1,
          rt = (mLen === 23 ? Math.pow(2, -24) - Math.pow(2, -77) : 0),
          i = isLE ? 0 : (nBytes - 1),
          d = isLE ? 1 : -1,
          s = value < 0 || (value === 0 && 1 / value < 0) ? 1 : 0;
    
      value = Math.abs(value);
    
      if (isNaN(value) || value === Infinity) {
        m = isNaN(value) ? 1 : 0;
        e = eMax;
      } else {
        e = Math.floor(Math.log(value) / Math.LN2);
        if (value * (c = Math.pow(2, -e)) < 1) {
          e--;
          c *= 2;
        }
        if (e + eBias >= 1) {
          value += rt / c;
        } else {
          value += rt * Math.pow(2, 1 - eBias);
        }
        if (value * c >= 2) {
          e++;
          c /= 2;
        }
    
        if (e + eBias >= eMax) {
          m = 0;
          e = eMax;
        } else if (e + eBias >= 1) {
          m = (value * c - 1) * Math.pow(2, mLen);
          e = e + eBias;
        } else {
          m = value * Math.pow(2, eBias - 1) * Math.pow(2, mLen);
          e = 0;
        }
      }
    
      for (; mLen >= 8; buffer[offset + i] = m & 0xff, i += d, m /= 256, mLen -= 8);
    
      e = (e << mLen) | m;
      eLen += mLen;
      for (; eLen > 0; buffer[offset + i] = e & 0xff, i += d, e /= 256, eLen -= 8);
    
      buffer[offset + i - d] |= s * 128;
    };
    
    },{}],6:[function(require,module,exports){
    (function (Buffer){
    var createHash = require('sha.js')
    
    var md5 = toConstructor(require('./md5'))
    var rmd160 = toConstructor(require('ripemd160'))
    
    function toConstructor (fn) {
      return function () {
        var buffers = []
        var m= {
          update: function (data, enc) {
            if(!Buffer.isBuffer(data)) data = new Buffer(data, enc)
            buffers.push(data)
            return this
          },
          digest: function (enc) {
            var buf = Buffer.concat(buffers)
            var r = fn(buf)
            buffers = null
            return enc ? r.toString(enc) : r
          }
        }
        return m
      }
    }
    
    module.exports = function (alg) {
      if('md5' === alg) return new md5()
      if('rmd160' === alg) return new rmd160()
      return createHash(alg)
    }
    
    }).call(this,require("buffer").Buffer)
    },{"./md5":10,"buffer":3,"ripemd160":11,"sha.js":13}],7:[function(require,module,exports){
    (function (Buffer){
    var createHash = require('./create-hash')
    
    var blocksize = 64
    var zeroBuffer = new Buffer(blocksize); zeroBuffer.fill(0)
    
    module.exports = Hmac
    
    function Hmac (alg, key) {
      if(!(this instanceof Hmac)) return new Hmac(alg, key)
      this._opad = opad
      this._alg = alg
    
      key = this._key = !Buffer.isBuffer(key) ? new Buffer(key) : key
    
      if(key.length > blocksize) {
        key = createHash(alg).update(key).digest()
      } else if(key.length < blocksize) {
        key = Buffer.concat([key, zeroBuffer], blocksize)
      }
    
      var ipad = this._ipad = new Buffer(blocksize)
      var opad = this._opad = new Buffer(blocksize)
    
      for(var i = 0; i < blocksize; i++) {
        ipad[i] = key[i] ^ 0x36
        opad[i] = key[i] ^ 0x5C
      }
    
      this._hash = createHash(alg).update(ipad)
    }
    
    Hmac.prototype.update = function (data, enc) {
      this._hash.update(data, enc)
      return this
    }
    
    Hmac.prototype.digest = function (enc) {
      var h = this._hash.digest()
      return createHash(this._alg).update(this._opad).update(h).digest(enc)
    }
    
    
    }).call(this,require("buffer").Buffer)
    },{"./create-hash":6,"buffer":3}],8:[function(require,module,exports){
    (function (Buffer){
    var intSize = 4;
    var zeroBuffer = new Buffer(intSize); zeroBuffer.fill(0);
    var chrsz = 8;
    
    function toArray(buf, bigEndian) {
      if ((buf.length % intSize) !== 0) {
        var len = buf.length + (intSize - (buf.length % intSize));
        buf = Buffer.concat([buf, zeroBuffer], len);
      }
    
      var arr = [];
      var fn = bigEndian ? buf.readInt32BE : buf.readInt32LE;
      for (var i = 0; i < buf.length; i += intSize) {
        arr.push(fn.call(buf, i));
      }
      return arr;
    }
    
    function toBuffer(arr, size, bigEndian) {
      var buf = new Buffer(size);
      var fn = bigEndian ? buf.writeInt32BE : buf.writeInt32LE;
      for (var i = 0; i < arr.length; i++) {
        fn.call(buf, arr[i], i * 4, true);
      }
      return buf;
    }
    
    function hash(buf, fn, hashSize, bigEndian) {
      if (!Buffer.isBuffer(buf)) buf = new Buffer(buf);
      var arr = fn(toArray(buf, bigEndian), buf.length * chrsz);
      return toBuffer(arr, hashSize, bigEndian);
    }
    
    module.exports = { hash: hash };
    
    }).call(this,require("buffer").Buffer)
    },{"buffer":3}],9:[function(require,module,exports){
    (function (Buffer){
    var rng = require('./rng')
    
    function error () {
      var m = [].slice.call(arguments).join(' ')
      throw new Error([
        m,
        'we accept pull requests',
        'http://github.com/dominictarr/crypto-browserify'
        ].join('\n'))
    }
    
    exports.createHash = require('./create-hash')
    
    exports.createHmac = require('./create-hmac')
    
    exports.randomBytes = function(size, callback) {
      if (callback && callback.call) {
        try {
          callback.call(this, undefined, new Buffer(rng(size)))
        } catch (err) { callback(err) }
      } else {
        return new Buffer(rng(size))
      }
    }
    
    function each(a, f) {
      for(var i in a)
        f(a[i], i)
    }
    
    exports.getHashes = function () {
      return ['sha1', 'sha256', 'md5', 'rmd160']
    
    }
    
    var p = require('./pbkdf2')(exports.createHmac)
    exports.pbkdf2 = p.pbkdf2
    exports.pbkdf2Sync = p.pbkdf2Sync
    
    
    // the least I can do is make error messages for the rest of the node.js/crypto api.
    each(['createCredentials'
    , 'createCipher'
    , 'createCipheriv'
    , 'createDecipher'
    , 'createDecipheriv'
    , 'createSign'
    , 'createVerify'
    , 'createDiffieHellman'
    ], function (name) {
      exports[name] = function () {
        error('sorry,', name, 'is not implemented yet')
      }
    })
    
    }).call(this,require("buffer").Buffer)
    },{"./create-hash":6,"./create-hmac":7,"./pbkdf2":17,"./rng":18,"buffer":3}],10:[function(require,module,exports){
    /*
     * A JavaScript implementation of the RSA Data Security, Inc. MD5 Message
     * Digest Algorithm, as defined in RFC 1321.
     * Version 2.1 Copyright (C) Paul Johnston 1999 - 2002.
     * Other contributors: Greg Holt, Andrew Kepert, Ydnar, Lostinet
     * Distributed under the BSD License
     * See http://pajhome.org.uk/crypt/md5 for more info.
     */
    
    var helpers = require('./helpers');
    
    /*
     * Calculate the MD5 of an array of little-endian words, and a bit length
     */
    function core_md5(x, len)
    {
      /* append padding */
      x[len >> 5] |= 0x80 << ((len) % 32);
      x[(((len + 64) >>> 9) << 4) + 14] = len;
    
      var a =  1732584193;
      var b = -271733879;
      var c = -1732584194;
      var d =  271733878;
    
      for(var i = 0; i < x.length; i += 16)
      {
        var olda = a;
        var oldb = b;
        var oldc = c;
        var oldd = d;
    
        a = md5_ff(a, b, c, d, x[i+ 0], 7 , -680876936);
        d = md5_ff(d, a, b, c, x[i+ 1], 12, -389564586);
        c = md5_ff(c, d, a, b, x[i+ 2], 17,  606105819);
        b = md5_ff(b, c, d, a, x[i+ 3], 22, -1044525330);
        a = md5_ff(a, b, c, d, x[i+ 4], 7 , -176418897);
        d = md5_ff(d, a, b, c, x[i+ 5], 12,  1200080426);
        c = md5_ff(c, d, a, b, x[i+ 6], 17, -1473231341);
        b = md5_ff(b, c, d, a, x[i+ 7], 22, -45705983);
        a = md5_ff(a, b, c, d, x[i+ 8], 7 ,  1770035416);
        d = md5_ff(d, a, b, c, x[i+ 9], 12, -1958414417);
        c = md5_ff(c, d, a, b, x[i+10], 17, -42063);
        b = md5_ff(b, c, d, a, x[i+11], 22, -1990404162);
        a = md5_ff(a, b, c, d, x[i+12], 7 ,  1804603682);
        d = md5_ff(d, a, b, c, x[i+13], 12, -40341101);
        c = md5_ff(c, d, a, b, x[i+14], 17, -1502002290);
        b = md5_ff(b, c, d, a, x[i+15], 22,  1236535329);
    
        a = md5_gg(a, b, c, d, x[i+ 1], 5 , -165796510);
        d = md5_gg(d, a, b, c, x[i+ 6], 9 , -1069501632);
        c = md5_gg(c, d, a, b, x[i+11], 14,  643717713);
        b = md5_gg(b, c, d, a, x[i+ 0], 20, -373897302);
        a = md5_gg(a, b, c, d, x[i+ 5], 5 , -701558691);
        d = md5_gg(d, a, b, c, x[i+10], 9 ,  38016083);
        c = md5_gg(c, d, a, b, x[i+15], 14, -660478335);
        b = md5_gg(b, c, d, a, x[i+ 4], 20, -405537848);
        a = md5_gg(a, b, c, d, x[i+ 9], 5 ,  568446438);
        d = md5_gg(d, a, b, c, x[i+14], 9 , -1019803690);
        c = md5_gg(c, d, a, b, x[i+ 3], 14, -187363961);
        b = md5_gg(b, c, d, a, x[i+ 8], 20,  1163531501);
        a = md5_gg(a, b, c, d, x[i+13], 5 , -1444681467);
        d = md5_gg(d, a, b, c, x[i+ 2], 9 , -51403784);
        c = md5_gg(c, d, a, b, x[i+ 7], 14,  1735328473);
        b = md5_gg(b, c, d, a, x[i+12], 20, -1926607734);
    
        a = md5_hh(a, b, c, d, x[i+ 5], 4 , -378558);
        d = md5_hh(d, a, b, c, x[i+ 8], 11, -2022574463);
        c = md5_hh(c, d, a, b, x[i+11], 16,  1839030562);
        b = md5_hh(b, c, d, a, x[i+14], 23, -35309556);
        a = md5_hh(a, b, c, d, x[i+ 1], 4 , -1530992060);
        d = md5_hh(d, a, b, c, x[i+ 4], 11,  1272893353);
        c = md5_hh(c, d, a, b, x[i+ 7], 16, -155497632);
        b = md5_hh(b, c, d, a, x[i+10], 23, -1094730640);
        a = md5_hh(a, b, c, d, x[i+13], 4 ,  681279174);
        d = md5_hh(d, a, b, c, x[i+ 0], 11, -358537222);
        c = md5_hh(c, d, a, b, x[i+ 3], 16, -722521979);
        b = md5_hh(b, c, d, a, x[i+ 6], 23,  76029189);
        a = md5_hh(a, b, c, d, x[i+ 9], 4 , -640364487);
        d = md5_hh(d, a, b, c, x[i+12], 11, -421815835);
        c = md5_hh(c, d, a, b, x[i+15], 16,  530742520);
        b = md5_hh(b, c, d, a, x[i+ 2], 23, -995338651);
    
        a = md5_ii(a, b, c, d, x[i+ 0], 6 , -198630844);
        d = md5_ii(d, a, b, c, x[i+ 7], 10,  1126891415);
        c = md5_ii(c, d, a, b, x[i+14], 15, -1416354905);
        b = md5_ii(b, c, d, a, x[i+ 5], 21, -57434055);
        a = md5_ii(a, b, c, d, x[i+12], 6 ,  1700485571);
        d = md5_ii(d, a, b, c, x[i+ 3], 10, -1894986606);
        c = md5_ii(c, d, a, b, x[i+10], 15, -1051523);
        b = md5_ii(b, c, d, a, x[i+ 1], 21, -2054922799);
        a = md5_ii(a, b, c, d, x[i+ 8], 6 ,  1873313359);
        d = md5_ii(d, a, b, c, x[i+15], 10, -30611744);
        c = md5_ii(c, d, a, b, x[i+ 6], 15, -1560198380);
        b = md5_ii(b, c, d, a, x[i+13], 21,  1309151649);
        a = md5_ii(a, b, c, d, x[i+ 4], 6 , -145523070);
        d = md5_ii(d, a, b, c, x[i+11], 10, -1120210379);
        c = md5_ii(c, d, a, b, x[i+ 2], 15,  718787259);
        b = md5_ii(b, c, d, a, x[i+ 9], 21, -343485551);
    
        a = safe_add(a, olda);
        b = safe_add(b, oldb);
        c = safe_add(c, oldc);
        d = safe_add(d, oldd);
      }
      return Array(a, b, c, d);
    
    }
    
    /*
     * These functions implement the four basic operations the algorithm uses.
     */
    function md5_cmn(q, a, b, x, s, t)
    {
      return safe_add(bit_rol(safe_add(safe_add(a, q), safe_add(x, t)), s),b);
    }
    function md5_ff(a, b, c, d, x, s, t)
    {
      return md5_cmn((b & c) | ((~b) & d), a, b, x, s, t);
    }
    function md5_gg(a, b, c, d, x, s, t)
    {
      return md5_cmn((b & d) | (c & (~d)), a, b, x, s, t);
    }
    function md5_hh(a, b, c, d, x, s, t)
    {
      return md5_cmn(b ^ c ^ d, a, b, x, s, t);
    }
    function md5_ii(a, b, c, d, x, s, t)
    {
      return md5_cmn(c ^ (b | (~d)), a, b, x, s, t);
    }
    
    /*
     * Add integers, wrapping at 2^32. This uses 16-bit operations internally
     * to work around bugs in some JS interpreters.
     */
    function safe_add(x, y)
    {
      var lsw = (x & 0xFFFF) + (y & 0xFFFF);
      var msw = (x >> 16) + (y >> 16) + (lsw >> 16);
      return (msw << 16) | (lsw & 0xFFFF);
    }
    
    /*
     * Bitwise rotate a 32-bit number to the left.
     */
    function bit_rol(num, cnt)
    {
      return (num << cnt) | (num >>> (32 - cnt));
    }
    
    module.exports = function md5(buf) {
      return helpers.hash(buf, core_md5, 16);
    };
    
    },{"./helpers":8}],11:[function(require,module,exports){
    (function (Buffer){
    
    module.exports = ripemd160
    
    
    
    /*
    CryptoJS v3.1.2
    code.google.com/p/crypto-js
    (c) 2009-2013 by Jeff Mott. All rights reserved.
    code.google.com/p/crypto-js/wiki/License
    */
    /** @preserve
    (c) 2012 by Cédric Mesnil. All rights reserved.
    
    Redistribution and use in source and binary forms, with or without modification, are permitted provided that the following conditions are met:
    
        - Redistributions of source code must retain the above copyright notice, this list of conditions and the following disclaimer.
        - Redistributions in binary form must reproduce the above copyright notice, this list of conditions and the following disclaimer in the documentation and/or other materials provided with the distribution.
    
    THIS SOFTWARE IS PROVIDED BY THE COPYRIGHT HOLDERS AND CONTRIBUTORS "AS IS" AND ANY EXPRESS OR IMPLIED WARRANTIES, INCLUDING, BUT NOT LIMITED TO, THE IMPLIED WARRANTIES OF MERCHANTABILITY AND FITNESS FOR A PARTICULAR PURPOSE ARE DISCLAIMED. IN NO EVENT SHALL THE COPYRIGHT HOLDER OR CONTRIBUTORS BE LIABLE FOR ANY DIRECT, INDIRECT, INCIDENTAL, SPECIAL, EXEMPLARY, OR CONSEQUENTIAL DAMAGES (INCLUDING, BUT NOT LIMITED TO, PROCUREMENT OF SUBSTITUTE GOODS OR SERVICES; LOSS OF USE, DATA, OR PROFITS; OR BUSINESS INTERRUPTION) HOWEVER CAUSED AND ON ANY THEORY OF LIABILITY, WHETHER IN CONTRACT, STRICT LIABILITY, OR TORT (INCLUDING NEGLIGENCE OR OTHERWISE) ARISING IN ANY WAY OUT OF THE USE OF THIS SOFTWARE, EVEN IF ADVISED OF THE POSSIBILITY OF SUCH DAMAGE.
    */
    
    // Constants table
    var zl = [
        0,  1,  2,  3,  4,  5,  6,  7,  8,  9, 10, 11, 12, 13, 14, 15,
        7,  4, 13,  1, 10,  6, 15,  3, 12,  0,  9,  5,  2, 14, 11,  8,
        3, 10, 14,  4,  9, 15,  8,  1,  2,  7,  0,  6, 13, 11,  5, 12,
        1,  9, 11, 10,  0,  8, 12,  4, 13,  3,  7, 15, 14,  5,  6,  2,
        4,  0,  5,  9,  7, 12,  2, 10, 14,  1,  3,  8, 11,  6, 15, 13];
    var zr = [
        5, 14,  7,  0,  9,  2, 11,  4, 13,  6, 15,  8,  1, 10,  3, 12,
        6, 11,  3,  7,  0, 13,  5, 10, 14, 15,  8, 12,  4,  9,  1,  2,
        15,  5,  1,  3,  7, 14,  6,  9, 11,  8, 12,  2, 10,  0,  4, 13,
        8,  6,  4,  1,  3, 11, 15,  0,  5, 12,  2, 13,  9,  7, 10, 14,
        12, 15, 10,  4,  1,  5,  8,  7,  6,  2, 13, 14,  0,  3,  9, 11];
    var sl = [
         11, 14, 15, 12,  5,  8,  7,  9, 11, 13, 14, 15,  6,  7,  9,  8,
        7, 6,   8, 13, 11,  9,  7, 15,  7, 12, 15,  9, 11,  7, 13, 12,
        11, 13,  6,  7, 14,  9, 13, 15, 14,  8, 13,  6,  5, 12,  7,  5,
          11, 12, 14, 15, 14, 15,  9,  8,  9, 14,  5,  6,  8,  6,  5, 12,
        9, 15,  5, 11,  6,  8, 13, 12,  5, 12, 13, 14, 11,  8,  5,  6 ];
    var sr = [
        8,  9,  9, 11, 13, 15, 15,  5,  7,  7,  8, 11, 14, 14, 12,  6,
        9, 13, 15,  7, 12,  8,  9, 11,  7,  7, 12,  7,  6, 15, 13, 11,
        9,  7, 15, 11,  8,  6,  6, 14, 12, 13,  5, 14, 13, 13,  7,  5,
        15,  5,  8, 11, 14, 14,  6, 14,  6,  9, 12,  9, 12,  5, 15,  8,
        8,  5, 12,  9, 12,  5, 14,  6,  8, 13,  6,  5, 15, 13, 11, 11 ];
    
    var hl =  [ 0x00000000, 0x5A827999, 0x6ED9EBA1, 0x8F1BBCDC, 0xA953FD4E];
    var hr =  [ 0x50A28BE6, 0x5C4DD124, 0x6D703EF3, 0x7A6D76E9, 0x00000000];
    
    var bytesToWords = function (bytes) {
      var words = [];
      for (var i = 0, b = 0; i < bytes.length; i++, b += 8) {
        words[b >>> 5] |= bytes[i] << (24 - b % 32);
      }
      return words;
    };
    
    var wordsToBytes = function (words) {
      var bytes = [];
      for (var b = 0; b < words.length * 32; b += 8) {
        bytes.push((words[b >>> 5] >>> (24 - b % 32)) & 0xFF);
      }
      return bytes;
    };
    
    var processBlock = function (H, M, offset) {
    
      // Swap endian
      for (var i = 0; i < 16; i++) {
        var offset_i = offset + i;
        var M_offset_i = M[offset_i];
    
        // Swap
        M[offset_i] = (
            (((M_offset_i << 8)  | (M_offset_i >>> 24)) & 0x00ff00ff) |
            (((M_offset_i << 24) | (M_offset_i >>> 8))  & 0xff00ff00)
        );
      }
    
      // Working variables
      var al, bl, cl, dl, el;
      var ar, br, cr, dr, er;
    
      ar = al = H[0];
      br = bl = H[1];
      cr = cl = H[2];
      dr = dl = H[3];
      er = el = H[4];
      // Computation
      var t;
      for (var i = 0; i < 80; i += 1) {
        t = (al +  M[offset+zl[i]])|0;
        if (i<16){
            t +=  f1(bl,cl,dl) + hl[0];
        } else if (i<32) {
            t +=  f2(bl,cl,dl) + hl[1];
        } else if (i<48) {
            t +=  f3(bl,cl,dl) + hl[2];
        } else if (i<64) {
            t +=  f4(bl,cl,dl) + hl[3];
        } else {// if (i<80) {
            t +=  f5(bl,cl,dl) + hl[4];
        }
        t = t|0;
        t =  rotl(t,sl[i]);
        t = (t+el)|0;
        al = el;
        el = dl;
        dl = rotl(cl, 10);
        cl = bl;
        bl = t;
    
        t = (ar + M[offset+zr[i]])|0;
        if (i<16){
            t +=  f5(br,cr,dr) + hr[0];
        } else if (i<32) {
            t +=  f4(br,cr,dr) + hr[1];
        } else if (i<48) {
            t +=  f3(br,cr,dr) + hr[2];
        } else if (i<64) {
            t +=  f2(br,cr,dr) + hr[3];
        } else {// if (i<80) {
            t +=  f1(br,cr,dr) + hr[4];
        }
        t = t|0;
        t =  rotl(t,sr[i]) ;
        t = (t+er)|0;
        ar = er;
        er = dr;
        dr = rotl(cr, 10);
        cr = br;
        br = t;
      }
      // Intermediate hash value
      t    = (H[1] + cl + dr)|0;
      H[1] = (H[2] + dl + er)|0;
      H[2] = (H[3] + el + ar)|0;
      H[3] = (H[4] + al + br)|0;
      H[4] = (H[0] + bl + cr)|0;
      H[0] =  t;
    };
    
    function f1(x, y, z) {
      return ((x) ^ (y) ^ (z));
    }
    
    function f2(x, y, z) {
      return (((x)&(y)) | ((~x)&(z)));
    }
    
    function f3(x, y, z) {
      return (((x) | (~(y))) ^ (z));
    }
    
    function f4(x, y, z) {
      return (((x) & (z)) | ((y)&(~(z))));
    }
    
    function f5(x, y, z) {
      return ((x) ^ ((y) |(~(z))));
    }
    
    function rotl(x,n) {
      return (x<<n) | (x>>>(32-n));
    }
    
    function ripemd160(message) {
      var H = [0x67452301, 0xEFCDAB89, 0x98BADCFE, 0x10325476, 0xC3D2E1F0];
    
      if (typeof message == 'string')
        message = new Buffer(message, 'utf8');
    
      var m = bytesToWords(message);
    
      var nBitsLeft = message.length * 8;
      var nBitsTotal = message.length * 8;
    
      // Add padding
      m[nBitsLeft >>> 5] |= 0x80 << (24 - nBitsLeft % 32);
      m[(((nBitsLeft + 64) >>> 9) << 4) + 14] = (
          (((nBitsTotal << 8)  | (nBitsTotal >>> 24)) & 0x00ff00ff) |
          (((nBitsTotal << 24) | (nBitsTotal >>> 8))  & 0xff00ff00)
      );
    
      for (var i=0 ; i<m.length; i += 16) {
        processBlock(H, m, i);
      }
    
      // Swap endian
      for (var i = 0; i < 5; i++) {
          // Shortcut
        var H_i = H[i];
    
        // Swap
        H[i] = (((H_i << 8)  | (H_i >>> 24)) & 0x00ff00ff) |
              (((H_i << 24) | (H_i >>> 8))  & 0xff00ff00);
      }
    
      var digestbytes = wordsToBytes(H);
      return new Buffer(digestbytes);
    }
    
    
    
    }).call(this,require("buffer").Buffer)
    },{"buffer":3}],12:[function(require,module,exports){
    var u = require('./util')
    var write = u.write
    var fill = u.zeroFill
    
    module.exports = function (Buffer) {
    
      //prototype class for hash functions
      function Hash (blockSize, finalSize) {
        this._block = new Buffer(blockSize) //new Uint32Array(blockSize/4)
        this._finalSize = finalSize
        this._blockSize = blockSize
        this._len = 0
        this._s = 0
      }
    
      Hash.prototype.init = function () {
        this._s = 0
        this._len = 0
      }
    
      function lengthOf(data, enc) {
        if(enc == null)     return data.byteLength || data.length
        if(enc == 'ascii' || enc == 'binary')  return data.length
        if(enc == 'hex')    return data.length/2
        if(enc == 'base64') return data.length/3
      }
    
      Hash.prototype.update = function (data, enc) {
        var bl = this._blockSize
    
        //I'd rather do this with a streaming encoder, like the opposite of
        //http://nodejs.org/api/string_decoder.html
        var length
          if(!enc && 'string' === typeof data)
            enc = 'utf8'
    
        if(enc) {
          if(enc === 'utf-8')
            enc = 'utf8'
    
          if(enc === 'base64' || enc === 'utf8')
            data = new Buffer(data, enc), enc = null
    
          length = lengthOf(data, enc)
        } else
          length = data.byteLength || data.length
    
        var l = this._len += length
        var s = this._s = (this._s || 0)
        var f = 0
        var buffer = this._block
        while(s < l) {
          var t = Math.min(length, f + bl - s%bl)
          write(buffer, data, enc, s%bl, f, t)
          var ch = (t - f);
          s += ch; f += ch
    
          if(!(s%bl))
            this._update(buffer)
        }
        this._s = s
    
        return this
    
      }
    
      Hash.prototype.digest = function (enc) {
        var bl = this._blockSize
        var fl = this._finalSize
        var len = this._len*8
    
        var x = this._block
    
        var bits = len % (bl*8)
    
        //add end marker, so that appending 0's creats a different hash.
        x[this._len % bl] = 0x80
        fill(this._block, this._len % bl + 1)
    
        if(bits >= fl*8) {
          this._update(this._block)
          u.zeroFill(this._block, 0)
        }
    
        //TODO: handle case where the bit length is > Math.pow(2, 29)
        x.writeInt32BE(len, fl + 4) //big endian
    
        var hash = this._update(this._block) || this._hash()
        if(enc == null) return hash
        return hash.toString(enc)
      }
    
      Hash.prototype._update = function () {
        throw new Error('_update must be implemented by subclass')
      }
    
      return Hash
    }
    
    },{"./util":16}],13:[function(require,module,exports){
    var exports = module.exports = function (alg) {
      var Alg = exports[alg]
      if(!Alg) throw new Error(alg + ' is not supported (we accept pull requests)')
      return new Alg()
    }
    
    var Buffer = require('buffer').Buffer
    var Hash   = require('./hash')(Buffer)
    
    exports.sha =
    exports.sha1 = require('./sha1')(Buffer, Hash)
    exports.sha256 = require('./sha256')(Buffer, Hash)
    
    },{"./hash":12,"./sha1":14,"./sha256":15,"buffer":3}],14:[function(require,module,exports){
    /*
     * A JavaScript implementation of the Secure Hash Algorithm, SHA-1, as defined
     * in FIPS PUB 180-1
     * Version 2.1a Copyright Paul Johnston 2000 - 2002.
     * Other contributors: Greg Holt, Andrew Kepert, Ydnar, Lostinet
     * Distributed under the BSD License
     * See http://pajhome.org.uk/crypt/md5 for details.
     */
    module.exports = function (Buffer, Hash) {
    
      var inherits = require('util').inherits
    
      inherits(Sha1, Hash)
    
      var A = 0|0
      var B = 4|0
      var C = 8|0
      var D = 12|0
      var E = 16|0
    
      var BE = false
      var LE = true
    
      var W = new Int32Array(80)
    
      var POOL = []
    
      function Sha1 () {
        if(POOL.length)
          return POOL.pop().init()
    
        if(!(this instanceof Sha1)) return new Sha1()
        this._w = W
        Hash.call(this, 16*4, 14*4)
      
        this._h = null
        this.init()
      }
    
      Sha1.prototype.init = function () {
        this._a = 0x67452301
        this._b = 0xefcdab89
        this._c = 0x98badcfe
        this._d = 0x10325476
        this._e = 0xc3d2e1f0
    
        Hash.prototype.init.call(this)
        return this
      }
    
      Sha1.prototype._POOL = POOL
    
      // assume that array is a Uint32Array with length=16,
      // and that if it is the last block, it already has the length and the 1 bit appended.
    
    
      var isDV = new Buffer(1) instanceof DataView
      function readInt32BE (X, i) {
        return isDV
          ? X.getInt32(i, false)
          : X.readInt32BE(i)
      }
    
      Sha1.prototype._update = function (array) {
    
        var X = this._block
        var h = this._h
        var a, b, c, d, e, _a, _b, _c, _d, _e
    
        a = _a = this._a
        b = _b = this._b
        c = _c = this._c
        d = _d = this._d
        e = _e = this._e
    
        var w = this._w
    
        for(var j = 0; j < 80; j++) {
          var W = w[j]
            = j < 16
            //? X.getInt32(j*4, false)
            //? readInt32BE(X, j*4) //*/ X.readInt32BE(j*4) //*/
            ? X.readInt32BE(j*4)
            : rol(w[j - 3] ^ w[j -  8] ^ w[j - 14] ^ w[j - 16], 1)
    
          var t =
            add(
              add(rol(a, 5), sha1_ft(j, b, c, d)),
              add(add(e, W), sha1_kt(j))
            );
    
          e = d
          d = c
          c = rol(b, 30)
          b = a
          a = t
        }
    
        this._a = add(a, _a)
        this._b = add(b, _b)
        this._c = add(c, _c)
        this._d = add(d, _d)
        this._e = add(e, _e)
      }
    
      Sha1.prototype._hash = function () {
        if(POOL.length < 100) POOL.push(this)
        var H = new Buffer(20)
        //console.log(this._a|0, this._b|0, this._c|0, this._d|0, this._e|0)
        H.writeInt32BE(this._a|0, A)
        H.writeInt32BE(this._b|0, B)
        H.writeInt32BE(this._c|0, C)
        H.writeInt32BE(this._d|0, D)
        H.writeInt32BE(this._e|0, E)
        return H
      }
    
      /*
       * Perform the appropriate triplet combination function for the current
       * iteration
       */
      function sha1_ft(t, b, c, d) {
        if(t < 20) return (b & c) | ((~b) & d);
        if(t < 40) return b ^ c ^ d;
        if(t < 60) return (b & c) | (b & d) | (c & d);
        return b ^ c ^ d;
      }
    
      /*
       * Determine the appropriate additive constant for the current iteration
       */
      function sha1_kt(t) {
        return (t < 20) ?  1518500249 : (t < 40) ?  1859775393 :
               (t < 60) ? -1894007588 : -899497514;
      }
    
      /*
       * Add integers, wrapping at 2^32. This uses 16-bit operations internally
       * to work around bugs in some JS interpreters.
       * //dominictarr: this is 10 years old, so maybe this can be dropped?)
       *
       */
      function add(x, y) {
        return (x + y ) | 0
      //lets see how this goes on testling.
      //  var lsw = (x & 0xFFFF) + (y & 0xFFFF);
      //  var msw = (x >> 16) + (y >> 16) + (lsw >> 16);
      //  return (msw << 16) | (lsw & 0xFFFF);
      }
    
      /*
       * Bitwise rotate a 32-bit number to the left.
       */
      function rol(num, cnt) {
        return (num << cnt) | (num >>> (32 - cnt));
      }
    
      return Sha1
    }
    
    },{"util":37}],15:[function(require,module,exports){
    
    /**
     * A JavaScript implementation of the Secure Hash Algorithm, SHA-256, as defined
     * in FIPS 180-2
     * Version 2.2-beta Copyright Angel Marin, Paul Johnston 2000 - 2009.
     * Other contributors: Greg Holt, Andrew Kepert, Ydnar, Lostinet
     *
     */
    
    var inherits = require('util').inherits
    var BE       = false
    var LE       = true
    var u        = require('./util')
    
    module.exports = function (Buffer, Hash) {
    
      var K = [
          0x428A2F98, 0x71374491, 0xB5C0FBCF, 0xE9B5DBA5,
          0x3956C25B, 0x59F111F1, 0x923F82A4, 0xAB1C5ED5,
          0xD807AA98, 0x12835B01, 0x243185BE, 0x550C7DC3,
          0x72BE5D74, 0x80DEB1FE, 0x9BDC06A7, 0xC19BF174,
          0xE49B69C1, 0xEFBE4786, 0x0FC19DC6, 0x240CA1CC,
          0x2DE92C6F, 0x4A7484AA, 0x5CB0A9DC, 0x76F988DA,
          0x983E5152, 0xA831C66D, 0xB00327C8, 0xBF597FC7,
          0xC6E00BF3, 0xD5A79147, 0x06CA6351, 0x14292967,
          0x27B70A85, 0x2E1B2138, 0x4D2C6DFC, 0x53380D13,
          0x650A7354, 0x766A0ABB, 0x81C2C92E, 0x92722C85,
          0xA2BFE8A1, 0xA81A664B, 0xC24B8B70, 0xC76C51A3,
          0xD192E819, 0xD6990624, 0xF40E3585, 0x106AA070,
          0x19A4C116, 0x1E376C08, 0x2748774C, 0x34B0BCB5,
          0x391C0CB3, 0x4ED8AA4A, 0x5B9CCA4F, 0x682E6FF3,
          0x748F82EE, 0x78A5636F, 0x84C87814, 0x8CC70208,
          0x90BEFFFA, 0xA4506CEB, 0xBEF9A3F7, 0xC67178F2
        ]
    
      inherits(Sha256, Hash)
      var W = new Array(64)
      var POOL = []
      function Sha256() {
        if(POOL.length) {
          //return POOL.shift().init()
        }
        //this._data = new Buffer(32)
    
        this.init()
    
        this._w = W //new Array(64)
    
        Hash.call(this, 16*4, 14*4)
      };
    
      Sha256.prototype.init = function () {
    
        this._a = 0x6a09e667|0
        this._b = 0xbb67ae85|0
        this._c = 0x3c6ef372|0
        this._d = 0xa54ff53a|0
        this._e = 0x510e527f|0
        this._f = 0x9b05688c|0
        this._g = 0x1f83d9ab|0
        this._h = 0x5be0cd19|0
    
        this._len = this._s = 0
    
        return this
      }
    
      var safe_add = function(x, y) {
        var lsw = (x & 0xFFFF) + (y & 0xFFFF);
        var msw = (x >> 16) + (y >> 16) + (lsw >> 16);
        return (msw << 16) | (lsw & 0xFFFF);
      }
    
      function S (X, n) {
        return (X >>> n) | (X << (32 - n));
      }
    
      function R (X, n) {
        return (X >>> n);
      }
    
      function Ch (x, y, z) {
        return ((x & y) ^ ((~x) & z));
      }
    
      function Maj (x, y, z) {
        return ((x & y) ^ (x & z) ^ (y & z));
      }
    
      function Sigma0256 (x) {
        return (S(x, 2) ^ S(x, 13) ^ S(x, 22));
      }
    
      function Sigma1256 (x) {
        return (S(x, 6) ^ S(x, 11) ^ S(x, 25));
      }
    
      function Gamma0256 (x) {
        return (S(x, 7) ^ S(x, 18) ^ R(x, 3));
      }
    
      function Gamma1256 (x) {
        return (S(x, 17) ^ S(x, 19) ^ R(x, 10));
      }
    
      Sha256.prototype._update = function(m) {
        var M = this._block
        var W = this._w
        var a, b, c, d, e, f, g, h
        var T1, T2
    
        a = this._a | 0
        b = this._b | 0
        c = this._c | 0
        d = this._d | 0
        e = this._e | 0
        f = this._f | 0
        g = this._g | 0
        h = this._h | 0
    
        for (var j = 0; j < 64; j++) {
          var w = W[j] = j < 16
            ? M.readInt32BE(j * 4)
            : Gamma1256(W[j - 2]) + W[j - 7] + Gamma0256(W[j - 15]) + W[j - 16]
    
          T1 = h + Sigma1256(e) + Ch(e, f, g) + K[j] + w
    
          T2 = Sigma0256(a) + Maj(a, b, c);
          h = g; g = f; f = e; e = d + T1; d = c; c = b; b = a; a = T1 + T2;
        }
    
        this._a = (a + this._a) | 0
        this._b = (b + this._b) | 0
        this._c = (c + this._c) | 0
        this._d = (d + this._d) | 0
        this._e = (e + this._e) | 0
        this._f = (f + this._f) | 0
        this._g = (g + this._g) | 0
        this._h = (h + this._h) | 0
    
      };
    
      Sha256.prototype._hash = function () {
        if(POOL.length < 10)
          POOL.push(this)
    
        var H = new Buffer(32)
    
        H.writeInt32BE(this._a,  0)
        H.writeInt32BE(this._b,  4)
        H.writeInt32BE(this._c,  8)
        H.writeInt32BE(this._d, 12)
        H.writeInt32BE(this._e, 16)
        H.writeInt32BE(this._f, 20)
        H.writeInt32BE(this._g, 24)
        H.writeInt32BE(this._h, 28)
    
        return H
      }
    
      return Sha256
    
    }
    
    },{"./util":16,"util":37}],16:[function(require,module,exports){
    exports.write = write
    exports.zeroFill = zeroFill
    
    exports.toString = toString
    
    function write (buffer, string, enc, start, from, to, LE) {
      var l = (to - from)
      if(enc === 'ascii' || enc === 'binary') {
        for( var i = 0; i < l; i++) {
          buffer[start + i] = string.charCodeAt(i + from)
        }
      }
      else if(enc == null) {
        for( var i = 0; i < l; i++) {
          buffer[start + i] = string[i + from]
        }
      }
      else if(enc === 'hex') {
        for(var i = 0; i < l; i++) {
          var j = from + i
          buffer[start + i] = parseInt(string[j*2] + string[(j*2)+1], 16)
        }
      }
      else if(enc === 'base64') {
        throw new Error('base64 encoding not yet supported')
      }
      else
        throw new Error(enc +' encoding not yet supported')
    }
    
    //always fill to the end!
    function zeroFill(buf, from) {
      for(var i = from; i < buf.length; i++)
        buf[i] = 0
    }
    
    
    },{}],17:[function(require,module,exports){
    (function (Buffer){
    // JavaScript PBKDF2 Implementation
    // Based on http://git.io/qsv2zw
    // Licensed under LGPL v3
    // Copyright (c) 2013 jduncanator
    
    var blocksize = 64
    var zeroBuffer = new Buffer(blocksize); zeroBuffer.fill(0)
    
    module.exports = function (createHmac, exports) {
      exports = exports || {}
    
      exports.pbkdf2 = function(password, salt, iterations, keylen, cb) {
        if('function' !== typeof cb)
          throw new Error('No callback provided to pbkdf2');
        setTimeout(function () {
          cb(null, exports.pbkdf2Sync(password, salt, iterations, keylen))
        })
      }
    
      exports.pbkdf2Sync = function(key, salt, iterations, keylen) {
        if('number' !== typeof iterations)
          throw new TypeError('Iterations not a number')
        if(iterations < 0)
          throw new TypeError('Bad iterations')
        if('number' !== typeof keylen)
          throw new TypeError('Key length not a number')
        if(keylen < 0)
          throw new TypeError('Bad key length')
    
        //stretch key to the correct length that hmac wants it,
        //otherwise this will happen every time hmac is called
        //twice per iteration.
        var key = !Buffer.isBuffer(key) ? new Buffer(key) : key
    
        if(key.length > blocksize) {
          key = createHash(alg).update(key).digest()
        } else if(key.length < blocksize) {
          key = Buffer.concat([key, zeroBuffer], blocksize)
        }
    
        var HMAC;
        var cplen, p = 0, i = 1, itmp = new Buffer(4), digtmp;
        var out = new Buffer(keylen);
        out.fill(0);
        while(keylen) {
          if(keylen > 20)
            cplen = 20;
          else
            cplen = keylen;
    
          /* We are unlikely to ever use more than 256 blocks (5120 bits!)
             * but just in case...
             */
            itmp[0] = (i >> 24) & 0xff;
            itmp[1] = (i >> 16) & 0xff;
              itmp[2] = (i >> 8) & 0xff;
              itmp[3] = i & 0xff;
    
              HMAC = createHmac('sha1', key);
              HMAC.update(salt)
              HMAC.update(itmp);
            digtmp = HMAC.digest();
            digtmp.copy(out, p, 0, cplen);
    
            for(var j = 1; j < iterations; j++) {
              HMAC = createHmac('sha1', key);
              HMAC.update(digtmp);
              digtmp = HMAC.digest();
              for(var k = 0; k < cplen; k++) {
                out[k] ^= digtmp[k];
              }
            }
          keylen -= cplen;
          i++;
          p += cplen;
        }
    
        return out;
      }
    
      return exports
    }
    
    }).call(this,require("buffer").Buffer)
    },{"buffer":3}],18:[function(require,module,exports){
    (function (Buffer){
    (function() {
      module.exports = function(size) {
        var bytes = new Buffer(size); //in browserify, this is an extended Uint8Array
        /* This will not work in older browsers.
         * See https://developer.mozilla.org/en-US/docs/Web/API/window.crypto.getRandomValues
         */
        crypto.getRandomValues(bytes);
        return bytes;
      }
    }())
    
    }).call(this,require("buffer").Buffer)
    },{"buffer":3}],19:[function(require,module,exports){
    // Copyright Joyent, Inc. and other Node contributors.
    //
    // Permission is hereby granted, free of charge, to any person obtaining a
    // copy of this software and associated documentation files (the
    // "Software"), to deal in the Software without restriction, including
    // without limitation the rights to use, copy, modify, merge, publish,
    // distribute, sublicense, and/or sell copies of the Software, and to permit
    // persons to whom the Software is furnished to do so, subject to the
    // following conditions:
    //
    // The above copyright notice and this permission notice shall be included
    // in all copies or substantial portions of the Software.
    //
    // THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS
    // OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF
    // MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN
    // NO EVENT SHALL THE AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM,
    // DAMAGES OR OTHER LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR
    // OTHERWISE, ARISING FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE
    // USE OR OTHER DEALINGS IN THE SOFTWARE.
    
    function EventEmitter() {
      this._events = this._events || {};
      this._maxListeners = this._maxListeners || undefined;
    }
    module.exports = EventEmitter;
    
    // Backwards-compat with node 0.10.x
    EventEmitter.EventEmitter = EventEmitter;
    
    EventEmitter.prototype._events = undefined;
    EventEmitter.prototype._maxListeners = undefined;
    
    // By default EventEmitters will print a warning if more than 10 listeners are
    // added to it. This is a useful default which helps finding memory leaks.
    EventEmitter.defaultMaxListeners = 10;
    
    // Obviously not all Emitters should be limited to 10. This function allows
    // that to be increased. Set to zero for unlimited.
    EventEmitter.prototype.setMaxListeners = function(n) {
      if (!isNumber(n) || n < 0 || isNaN(n))
        throw TypeError('n must be a positive number');
      this._maxListeners = n;
      return this;
    };
    
    EventEmitter.prototype.emit = function(type) {
      var er, handler, len, args, i, listeners;
    
      if (!this._events)
        this._events = {};
    
      // If there is no 'error' event listener then throw.
      if (type === 'error') {
        if (!this._events.error ||
            (isObject(this._events.error) && !this._events.error.length)) {
          er = arguments[1];
          if (er instanceof Error) {
            throw er; // Unhandled 'error' event
          } else {
            throw TypeError('Uncaught, unspecified "error" event.');
          }
          return false;
        }
      }
    
      handler = this._events[type];
    
      if (isUndefined(handler))
        return false;
    
      if (isFunction(handler)) {
        switch (arguments.length) {
          // fast cases
          case 1:
            handler.call(this);
            break;
          case 2:
            handler.call(this, arguments[1]);
            break;
          case 3:
            handler.call(this, arguments[1], arguments[2]);
            break;
          // slower
          default:
            len = arguments.length;
            args = new Array(len - 1);
            for (i = 1; i < len; i++)
              args[i - 1] = arguments[i];
            handler.apply(this, args);
        }
      } else if (isObject(handler)) {
        len = arguments.length;
        args = new Array(len - 1);
        for (i = 1; i < len; i++)
          args[i - 1] = arguments[i];
    
        listeners = handler.slice();
        len = listeners.length;
        for (i = 0; i < len; i++)
          listeners[i].apply(this, args);
      }
    
      return true;
    };
    
    EventEmitter.prototype.addListener = function(type, listener) {
      var m;
    
      if (!isFunction(listener))
        throw TypeError('listener must be a function');
    
      if (!this._events)
        this._events = {};
    
      // To avoid recursion in the case that type === "newListener"! Before
      // adding it to the listeners, first emit "newListener".
      if (this._events.newListener)
        this.emit('newListener', type,
                  isFunction(listener.listener) ?
                  listener.listener : listener);
    
      if (!this._events[type])
        // Optimize the case of one listener. Don't need the extra array object.
        this._events[type] = listener;
      else if (isObject(this._events[type]))
        // If we've already got an array, just append.
        this._events[type].push(listener);
      else
        // Adding the second element, need to change to array.
        this._events[type] = [this._events[type], listener];
    
      // Check for listener leak
      if (isObject(this._events[type]) && !this._events[type].warned) {
        var m;
        if (!isUndefined(this._maxListeners)) {
          m = this._maxListeners;
        } else {
          m = EventEmitter.defaultMaxListeners;
        }
    
        if (m && m > 0 && this._events[type].length > m) {
          this._events[type].warned = true;
          console.error('(node) warning: possible EventEmitter memory ' +
                        'leak detected. %d listeners added. ' +
                        'Use emitter.setMaxListeners() to increase limit.',
                        this._events[type].length);
          if (typeof console.trace === 'function') {
            // not supported in IE 10
            console.trace();
          }
        }
      }
    
      return this;
    };
    
    EventEmitter.prototype.on = EventEmitter.prototype.addListener;
    
    EventEmitter.prototype.once = function(type, listener) {
      if (!isFunction(listener))
        throw TypeError('listener must be a function');
    
      var fired = false;
    
      function g() {
        this.removeListener(type, g);
    
        if (!fired) {
          fired = true;
          listener.apply(this, arguments);
        }
      }
    
      g.listener = listener;
      this.on(type, g);
    
      return this;
    };
    
    // emits a 'removeListener' event iff the listener was removed
    EventEmitter.prototype.removeListener = function(type, listener) {
      var list, position, length, i;
    
      if (!isFunction(listener))
        throw TypeError('listener must be a function');
    
      if (!this._events || !this._events[type])
        return this;
    
      list = this._events[type];
      length = list.length;
      position = -1;
    
      if (list === listener ||
          (isFunction(list.listener) && list.listener === listener)) {
        delete this._events[type];
        if (this._events.removeListener)
          this.emit('removeListener', type, listener);
    
      } else if (isObject(list)) {
        for (i = length; i-- > 0;) {
          if (list[i] === listener ||
              (list[i].listener && list[i].listener === listener)) {
            position = i;
            break;
          }
        }
    
        if (position < 0)
          return this;
    
        if (list.length === 1) {
          list.length = 0;
          delete this._events[type];
        } else {
          list.splice(position, 1);
        }
    
        if (this._events.removeListener)
          this.emit('removeListener', type, listener);
      }
    
      return this;
    };
    
    EventEmitter.prototype.removeAllListeners = function(type) {
      var key, listeners;
    
      if (!this._events)
        return this;
    
      // not listening for removeListener, no need to emit
      if (!this._events.removeListener) {
        if (arguments.length === 0)
          this._events = {};
        else if (this._events[type])
          delete this._events[type];
        return this;
      }
    
      // emit removeListener for all listeners on all events
      if (arguments.length === 0) {
        for (key in this._events) {
          if (key === 'removeListener') continue;
          this.removeAllListeners(key);
        }
        this.removeAllListeners('removeListener');
        this._events = {};
        return this;
      }
    
      listeners = this._events[type];
    
      if (isFunction(listeners)) {
        this.removeListener(type, listeners);
      } else {
        // LIFO order
        while (listeners.length)
          this.removeListener(type, listeners[listeners.length - 1]);
      }
      delete this._events[type];
    
      return this;
    };
    
    EventEmitter.prototype.listeners = function(type) {
      var ret;
      if (!this._events || !this._events[type])
        ret = [];
      else if (isFunction(this._events[type]))
        ret = [this._events[type]];
      else
        ret = this._events[type].slice();
      return ret;
    };
    
    EventEmitter.listenerCount = function(emitter, type) {
      var ret;
      if (!emitter._events || !emitter._events[type])
        ret = 0;
      else if (isFunction(emitter._events[type]))
        ret = 1;
      else
        ret = emitter._events[type].length;
      return ret;
    };
    
    function isFunction(arg) {
      return typeof arg === 'function';
    }
    
    function isNumber(arg) {
      return typeof arg === 'number';
    }
    
    function isObject(arg) {
      return typeof arg === 'object' && arg !== null;
    }
    
    function isUndefined(arg) {
      return arg === void 0;
    }
    
    },{}],20:[function(require,module,exports){
    if (typeof Object.create === 'function') {
      // implementation from standard node.js 'util' module
      module.exports = function inherits(ctor, superCtor) {
        ctor.super_ = superCtor
        ctor.prototype = Object.create(superCtor.prototype, {
          constructor: {
            value: ctor,
            enumerable: false,
            writable: true,
            configurable: true
          }
        });
      };
    } else {
      // old school shim for old browsers
      module.exports = function inherits(ctor, superCtor) {
        ctor.super_ = superCtor
        var TempCtor = function () {}
        TempCtor.prototype = superCtor.prototype
        ctor.prototype = new TempCtor()
        ctor.prototype.constructor = ctor
      }
    }
    
    },{}],21:[function(require,module,exports){
    module.exports = Array.isArray || function (arr) {
      return Object.prototype.toString.call(arr) == '[object Array]';
    };
    
    },{}],22:[function(require,module,exports){
    // shim for using process in browser
    
    var process = module.exports = {};
    
    process.nextTick = (function () {
        var canSetImmediate = typeof window !== 'undefined'
        && window.setImmediate;
        var canPost = typeof window !== 'undefined'
        && window.postMessage && window.addEventListener
        ;
    
        if (canSetImmediate) {
            return function (f) { return window.setImmediate(f) };
        }
    
        if (canPost) {
            var queue = [];
            window.addEventListener('message', function (ev) {
                var source = ev.source;
                if ((source === window || source === null) && ev.data === 'process-tick') {
                    ev.stopPropagation();
                    if (queue.length > 0) {
                        var fn = queue.shift();
                        fn();
                    }
                }
            }, true);
    
            return function nextTick(fn) {
                queue.push(fn);
                window.postMessage('process-tick', '*');
            };
        }
    
        return function nextTick(fn) {
            setTimeout(fn, 0);
        };
    })();
    
    process.title = 'browser';
    process.browser = true;
    process.env = {};
    process.argv = [];
    
    function noop() {}
    
    process.on = noop;
    process.addListener = noop;
    process.once = noop;
    process.off = noop;
    process.removeListener = noop;
    process.removeAllListeners = noop;
    process.emit = noop;
    
    process.binding = function (name) {
        throw new Error('process.binding is not supported');
    }
    
    // TODO(shtylman)
    process.cwd = function () { return '/' };
    process.chdir = function (dir) {
        throw new Error('process.chdir is not supported');
    };
    
    },{}],23:[function(require,module,exports){
    module.exports = require("./lib/_stream_duplex.js")
    
    },{"./lib/_stream_duplex.js":24}],24:[function(require,module,exports){
    (function (process){
    // Copyright Joyent, Inc. and other Node contributors.
    //
    // Permission is hereby granted, free of charge, to any person obtaining a
    // copy of this software and associated documentation files (the
    // "Software"), to deal in the Software without restriction, including
    // without limitation the rights to use, copy, modify, merge, publish,
    // distribute, sublicense, and/or sell copies of the Software, and to permit
    // persons to whom the Software is furnished to do so, subject to the
    // following conditions:
    //
    // The above copyright notice and this permission notice shall be included
    // in all copies or substantial portions of the Software.
    //
    // THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS
    // OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF
    // MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN
    // NO EVENT SHALL THE AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM,
    // DAMAGES OR OTHER LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR
    // OTHERWISE, ARISING FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE
    // USE OR OTHER DEALINGS IN THE SOFTWARE.
    
    // a duplex stream is just a stream that is both readable and writable.
    // Since JS doesn't have multiple prototypal inheritance, this class
    // prototypally inherits from Readable, and then parasitically from
    // Writable.
    
    module.exports = Duplex;
    
    /*<replacement>*/
    var objectKeys = Object.keys || function (obj) {
      var keys = [];
      for (var key in obj) keys.push(key);
      return keys;
    }
    /*</replacement>*/
    
    
    /*<replacement>*/
    var util = require('core-util-is');
    util.inherits = require('inherits');
    /*</replacement>*/
    
    var Readable = require('./_stream_readable');
    var Writable = require('./_stream_writable');
    
    util.inherits(Duplex, Readable);
    
    forEach(objectKeys(Writable.prototype), function(method) {
      if (!Duplex.prototype[method])
        Duplex.prototype[method] = Writable.prototype[method];
    });
    
    function Duplex(options) {
      if (!(this instanceof Duplex))
        return new Duplex(options);
    
      Readable.call(this, options);
      Writable.call(this, options);
    
      if (options && options.readable === false)
        this.readable = false;
    
      if (options && options.writable === false)
        this.writable = false;
    
      this.allowHalfOpen = true;
      if (options && options.allowHalfOpen === false)
        this.allowHalfOpen = false;
    
      this.once('end', onend);
    }
    
    // the no-half-open enforcer
    function onend() {
      // if we allow half-open state, or if the writable side ended,
      // then we're ok.
      if (this.allowHalfOpen || this._writableState.ended)
        return;
    
      // no more data can be written.
      // But allow more writes to happen in this tick.
      process.nextTick(this.end.bind(this));
    }
    
    function forEach (xs, f) {
      for (var i = 0, l = xs.length; i < l; i++) {
        f(xs[i], i);
      }
    }
    
    }).call(this,require('_process'))
    },{"./_stream_readable":26,"./_stream_writable":28,"_process":22,"core-util-is":29,"inherits":20}],25:[function(require,module,exports){
    // Copyright Joyent, Inc. and other Node contributors.
    //
    // Permission is hereby granted, free of charge, to any person obtaining a
    // copy of this software and associated documentation files (the
    // "Software"), to deal in the Software without restriction, including
    // without limitation the rights to use, copy, modify, merge, publish,
    // distribute, sublicense, and/or sell copies of the Software, and to permit
    // persons to whom the Software is furnished to do so, subject to the
    // following conditions:
    //
    // The above copyright notice and this permission notice shall be included
    // in all copies or substantial portions of the Software.
    //
    // THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS
    // OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF
    // MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN
    // NO EVENT SHALL THE AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM,
    // DAMAGES OR OTHER LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR
    // OTHERWISE, ARISING FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE
    // USE OR OTHER DEALINGS IN THE SOFTWARE.
    
    // a passthrough stream.
    // basically just the most minimal sort of Transform stream.
    // Every written chunk gets output as-is.
    
    module.exports = PassThrough;
    
    var Transform = require('./_stream_transform');
    
    /*<replacement>*/
    var util = require('core-util-is');
    util.inherits = require('inherits');
    /*</replacement>*/
    
    util.inherits(PassThrough, Transform);
    
    function PassThrough(options) {
      if (!(this instanceof PassThrough))
        return new PassThrough(options);
    
      Transform.call(this, options);
    }
    
    PassThrough.prototype._transform = function(chunk, encoding, cb) {
      cb(null, chunk);
    };
    
    },{"./_stream_transform":27,"core-util-is":29,"inherits":20}],26:[function(require,module,exports){
    (function (process){
    // Copyright Joyent, Inc. and other Node contributors.
    //
    // Permission is hereby granted, free of charge, to any person obtaining a
    // copy of this software and associated documentation files (the
    // "Software"), to deal in the Software without restriction, including
    // without limitation the rights to use, copy, modify, merge, publish,
    // distribute, sublicense, and/or sell copies of the Software, and to permit
    // persons to whom the Software is furnished to do so, subject to the
    // following conditions:
    //
    // The above copyright notice and this permission notice shall be included
    // in all copies or substantial portions of the Software.
    //
    // THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS
    // OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF
    // MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN
    // NO EVENT SHALL THE AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM,
    // DAMAGES OR OTHER LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR
    // OTHERWISE, ARISING FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE
    // USE OR OTHER DEALINGS IN THE SOFTWARE.
    
    module.exports = Readable;
    
    /*<replacement>*/
    var isArray = require('isarray');
    /*</replacement>*/
    
    
    /*<replacement>*/
    var Buffer = require('buffer').Buffer;
    /*</replacement>*/
    
    Readable.ReadableState = ReadableState;
    
    var EE = require('events').EventEmitter;
    
    /*<replacement>*/
    if (!EE.listenerCount) EE.listenerCount = function(emitter, type) {
      return emitter.listeners(type).length;
    };
    /*</replacement>*/
    
    var Stream = require('stream');
    
    /*<replacement>*/
    var util = require('core-util-is');
    util.inherits = require('inherits');
    /*</replacement>*/
    
    var StringDecoder;
    
    util.inherits(Readable, Stream);
    
    function ReadableState(options, stream) {
      options = options || {};
    
      // the point at which it stops calling _read() to fill the buffer
      // Note: 0 is a valid value, means "don't call _read preemptively ever"
      var hwm = options.highWaterMark;
      this.highWaterMark = (hwm || hwm === 0) ? hwm : 16 * 1024;
    
      // cast to ints.
      this.highWaterMark = ~~this.highWaterMark;
    
      this.buffer = [];
      this.length = 0;
      this.pipes = null;
      this.pipesCount = 0;
      this.flowing = false;
      this.ended = false;
      this.endEmitted = false;
      this.reading = false;
    
      // In streams that never have any data, and do push(null) right away,
      // the consumer can miss the 'end' event if they do some I/O before
      // consuming the stream.  So, we don't emit('end') until some reading
      // happens.
      this.calledRead = false;
    
      // a flag to be able to tell if the onwrite cb is called immediately,
      // or on a later tick.  We set this to true at first, becuase any
      // actions that shouldn't happen until "later" should generally also
      // not happen before the first write call.
      this.sync = true;
    
      // whenever we return null, then we set a flag to say
      // that we're awaiting a 'readable' event emission.
      this.needReadable = false;
      this.emittedReadable = false;
      this.readableListening = false;
    
    
      // object stream flag. Used to make read(n) ignore n and to
      // make all the buffer merging and length checks go away
      this.objectMode = !!options.objectMode;
    
      // Crypto is kind of old and crusty.  Historically, its default string
      // encoding is 'binary' so we have to make this configurable.
      // Everything else in the universe uses 'utf8', though.
      this.defaultEncoding = options.defaultEncoding || 'utf8';
    
      // when piping, we only care about 'readable' events that happen
      // after read()ing all the bytes and not getting any pushback.
      this.ranOut = false;
    
      // the number of writers that are awaiting a drain event in .pipe()s
      this.awaitDrain = 0;
    
      // if true, a maybeReadMore has been scheduled
      this.readingMore = false;
    
      this.decoder = null;
      this.encoding = null;
      if (options.encoding) {
        if (!StringDecoder)
          StringDecoder = require('string_decoder/').StringDecoder;
        this.decoder = new StringDecoder(options.encoding);
        this.encoding = options.encoding;
      }
    }
    
    function Readable(options) {
      if (!(this instanceof Readable))
        return new Readable(options);
    
      this._readableState = new ReadableState(options, this);
    
      // legacy
      this.readable = true;
    
      Stream.call(this);
    }
    
    // Manually shove something into the read() buffer.
    // This returns true if the highWaterMark has not been hit yet,
    // similar to how Writable.write() returns true if you should
    // write() some more.
    Readable.prototype.push = function(chunk, encoding) {
      var state = this._readableState;
    
      if (typeof chunk === 'string' && !state.objectMode) {
        encoding = encoding || state.defaultEncoding;
        if (encoding !== state.encoding) {
          chunk = new Buffer(chunk, encoding);
          encoding = '';
        }
      }
    
      return readableAddChunk(this, state, chunk, encoding, false);
    };
    
    // Unshift should *always* be something directly out of read()
    Readable.prototype.unshift = function(chunk) {
      var state = this._readableState;
      return readableAddChunk(this, state, chunk, '', true);
    };
    
    function readableAddChunk(stream, state, chunk, encoding, addToFront) {
      var er = chunkInvalid(state, chunk);
      if (er) {
        stream.emit('error', er);
      } else if (chunk === null || chunk === undefined) {
        state.reading = false;
        if (!state.ended)
          onEofChunk(stream, state);
      } else if (state.objectMode || chunk && chunk.length > 0) {
        if (state.ended && !addToFront) {
          var e = new Error('stream.push() after EOF');
          stream.emit('error', e);
        } else if (state.endEmitted && addToFront) {
          var e = new Error('stream.unshift() after end event');
          stream.emit('error', e);
        } else {
          if (state.decoder && !addToFront && !encoding)
            chunk = state.decoder.write(chunk);
    
          // update the buffer info.
          state.length += state.objectMode ? 1 : chunk.length;
          if (addToFront) {
            state.buffer.unshift(chunk);
          } else {
            state.reading = false;
            state.buffer.push(chunk);
          }
    
          if (state.needReadable)
            emitReadable(stream);
    
          maybeReadMore(stream, state);
        }
      } else if (!addToFront) {
        state.reading = false;
      }
    
      return needMoreData(state);
    }
    
    
    
    // if it's past the high water mark, we can push in some more.
    // Also, if we have no data yet, we can stand some
    // more bytes.  This is to work around cases where hwm=0,
    // such as the repl.  Also, if the push() triggered a
    // readable event, and the user called read(largeNumber) such that
    // needReadable was set, then we ought to push more, so that another
    // 'readable' event will be triggered.
    function needMoreData(state) {
      return !state.ended &&
             (state.needReadable ||
              state.length < state.highWaterMark ||
              state.length === 0);
    }
    
    // backwards compatibility.
    Readable.prototype.setEncoding = function(enc) {
      if (!StringDecoder)
        StringDecoder = require('string_decoder/').StringDecoder;
      this._readableState.decoder = new StringDecoder(enc);
      this._readableState.encoding = enc;
    };
    
    // Don't raise the hwm > 128MB
    var MAX_HWM = 0x800000;
    function roundUpToNextPowerOf2(n) {
      if (n >= MAX_HWM) {
        n = MAX_HWM;
      } else {
        // Get the next highest power of 2
        n--;
        for (var p = 1; p < 32; p <<= 1) n |= n >> p;
        n++;
      }
      return n;
    }
    
    function howMuchToRead(n, state) {
      if (state.length === 0 && state.ended)
        return 0;
    
      if (state.objectMode)
        return n === 0 ? 0 : 1;
    
      if (n === null || isNaN(n)) {
        // only flow one buffer at a time
        if (state.flowing && state.buffer.length)
          return state.buffer[0].length;
        else
          return state.length;
      }
    
      if (n <= 0)
        return 0;
    
      // If we're asking for more than the target buffer level,
      // then raise the water mark.  Bump up to the next highest
      // power of 2, to prevent increasing it excessively in tiny
      // amounts.
      if (n > state.highWaterMark)
        state.highWaterMark = roundUpToNextPowerOf2(n);
    
      // don't have that much.  return null, unless we've ended.
      if (n > state.length) {
        if (!state.ended) {
          state.needReadable = true;
          return 0;
        } else
          return state.length;
      }
    
      return n;
    }
    
    // you can override either this method, or the async _read(n) below.
    Readable.prototype.read = function(n) {
      var state = this._readableState;
      state.calledRead = true;
      var nOrig = n;
      var ret;
    
      if (typeof n !== 'number' || n > 0)
        state.emittedReadable = false;
    
      // if we're doing read(0) to trigger a readable event, but we
      // already have a bunch of data in the buffer, then just trigger
      // the 'readable' event and move on.
      if (n === 0 &&
          state.needReadable &&
          (state.length >= state.highWaterMark || state.ended)) {
        emitReadable(this);
        return null;
      }
    
      n = howMuchToRead(n, state);
    
      // if we've ended, and we're now clear, then finish it up.
      if (n === 0 && state.ended) {
        ret = null;
    
        // In cases where the decoder did not receive enough data
        // to produce a full chunk, then immediately received an
        // EOF, state.buffer will contain [<Buffer >, <Buffer 00 ...>].
        // howMuchToRead will see this and coerce the amount to
        // read to zero (because it's looking at the length of the
        // first <Buffer > in state.buffer), and we'll end up here.
        //
        // This can only happen via state.decoder -- no other venue
        // exists for pushing a zero-length chunk into state.buffer
        // and triggering this behavior. In this case, we return our
        // remaining data and end the stream, if appropriate.
        if (state.length > 0 && state.decoder) {
          ret = fromList(n, state);
          state.length -= ret.length;
        }
    
        if (state.length === 0)
          endReadable(this);
    
        return ret;
      }
    
      // All the actual chunk generation logic needs to be
      // *below* the call to _read.  The reason is that in certain
      // synthetic stream cases, such as passthrough streams, _read
      // may be a completely synchronous operation which may change
      // the state of the read buffer, providing enough data when
      // before there was *not* enough.
      //
      // So, the steps are:
      // 1. Figure out what the state of things will be after we do
      // a read from the buffer.
      //
      // 2. If that resulting state will trigger a _read, then call _read.
      // Note that this may be asynchronous, or synchronous.  Yes, it is
      // deeply ugly to write APIs this way, but that still doesn't mean
      // that the Readable class should behave improperly, as streams are
      // designed to be sync/async agnostic.
      // Take note if the _read call is sync or async (ie, if the read call
      // has returned yet), so that we know whether or not it's safe to emit
      // 'readable' etc.
      //
      // 3. Actually pull the requested chunks out of the buffer and return.
    
      // if we need a readable event, then we need to do some reading.
      var doRead = state.needReadable;
    
      // if we currently have less than the highWaterMark, then also read some
      if (state.length - n <= state.highWaterMark)
        doRead = true;
    
      // however, if we've ended, then there's no point, and if we're already
      // reading, then it's unnecessary.
      if (state.ended || state.reading)
        doRead = false;
    
      if (doRead) {
        state.reading = true;
        state.sync = true;
        // if the length is currently zero, then we *need* a readable event.
        if (state.length === 0)
          state.needReadable = true;
        // call internal read method
        this._read(state.highWaterMark);
        state.sync = false;
      }
    
      // If _read called its callback synchronously, then `reading`
      // will be false, and we need to re-evaluate how much data we
      // can return to the user.
      if (doRead && !state.reading)
        n = howMuchToRead(nOrig, state);
    
      if (n > 0)
        ret = fromList(n, state);
      else
        ret = null;
    
      if (ret === null) {
        state.needReadable = true;
        n = 0;
      }
    
      state.length -= n;
    
      // If we have nothing in the buffer, then we want to know
      // as soon as we *do* get something into the buffer.
      if (state.length === 0 && !state.ended)
        state.needReadable = true;
    
      // If we happened to read() exactly the remaining amount in the
      // buffer, and the EOF has been seen at this point, then make sure
      // that we emit 'end' on the very next tick.
      if (state.ended && !state.endEmitted && state.length === 0)
        endReadable(this);
    
      return ret;
    };
    
    function chunkInvalid(state, chunk) {
      var er = null;
      if (!Buffer.isBuffer(chunk) &&
          'string' !== typeof chunk &&
          chunk !== null &&
          chunk !== undefined &&
          !state.objectMode) {
        er = new TypeError('Invalid non-string/buffer chunk');
      }
      return er;
    }
    
    
    function onEofChunk(stream, state) {
      if (state.decoder && !state.ended) {
        var chunk = state.decoder.end();
        if (chunk && chunk.length) {
          state.buffer.push(chunk);
          state.length += state.objectMode ? 1 : chunk.length;
        }
      }
      state.ended = true;
    
      // if we've ended and we have some data left, then emit
      // 'readable' now to make sure it gets picked up.
      if (state.length > 0)
        emitReadable(stream);
      else
        endReadable(stream);
    }
    
    // Don't emit readable right away in sync mode, because this can trigger
    // another read() call => stack overflow.  This way, it might trigger
    // a nextTick recursion warning, but that's not so bad.
    function emitReadable(stream) {
      var state = stream._readableState;
      state.needReadable = false;
      if (state.emittedReadable)
        return;
    
      state.emittedReadable = true;
      if (state.sync)
        process.nextTick(function() {
          emitReadable_(stream);
        });
      else
        emitReadable_(stream);
    }
    
    function emitReadable_(stream) {
      stream.emit('readable');
    }
    
    
    // at this point, the user has presumably seen the 'readable' event,
    // and called read() to consume some data.  that may have triggered
    // in turn another _read(n) call, in which case reading = true if
    // it's in progress.
    // However, if we're not ended, or reading, and the length < hwm,
    // then go ahead and try to read some more preemptively.
    function maybeReadMore(stream, state) {
      if (!state.readingMore) {
        state.readingMore = true;
        process.nextTick(function() {
          maybeReadMore_(stream, state);
        });
      }
    }
    
    function maybeReadMore_(stream, state) {
      var len = state.length;
      while (!state.reading && !state.flowing && !state.ended &&
             state.length < state.highWaterMark) {
        stream.read(0);
        if (len === state.length)
          // didn't get any data, stop spinning.
          break;
        else
          len = state.length;
      }
      state.readingMore = false;
    }
    
    // abstract method.  to be overridden in specific implementation classes.
    // call cb(er, data) where data is <= n in length.
    // for virtual (non-string, non-buffer) streams, "length" is somewhat
    // arbitrary, and perhaps not very meaningful.
    Readable.prototype._read = function(n) {
      this.emit('error', new Error('not implemented'));
    };
    
    Readable.prototype.pipe = function(dest, pipeOpts) {
      var src = this;
      var state = this._readableState;
    
      switch (state.pipesCount) {
        case 0:
          state.pipes = dest;
          break;
        case 1:
          state.pipes = [state.pipes, dest];
          break;
        default:
          state.pipes.push(dest);
          break;
      }
      state.pipesCount += 1;
    
      var doEnd = (!pipeOpts || pipeOpts.end !== false) &&
                  dest !== process.stdout &&
                  dest !== process.stderr;
    
      var endFn = doEnd ? onend : cleanup;
      if (state.endEmitted)
        process.nextTick(endFn);
      else
        src.once('end', endFn);
    
      dest.on('unpipe', onunpipe);
      function onunpipe(readable) {
        if (readable !== src) return;
        cleanup();
      }
    
      function onend() {
        dest.end();
      }
    
      // when the dest drains, it reduces the awaitDrain counter
      // on the source.  This would be more elegant with a .once()
      // handler in flow(), but adding and removing repeatedly is
      // too slow.
      var ondrain = pipeOnDrain(src);
      dest.on('drain', ondrain);
    
      function cleanup() {
        // cleanup event handlers once the pipe is broken
        dest.removeListener('close', onclose);
        dest.removeListener('finish', onfinish);
        dest.removeListener('drain', ondrain);
        dest.removeListener('error', onerror);
        dest.removeListener('unpipe', onunpipe);
        src.removeListener('end', onend);
        src.removeListener('end', cleanup);
    
        // if the reader is waiting for a drain event from this
        // specific writer, then it would cause it to never start
        // flowing again.
        // So, if this is awaiting a drain, then we just call it now.
        // If we don't know, then assume that we are waiting for one.
        if (!dest._writableState || dest._writableState.needDrain)
          ondrain();
      }
    
      // if the dest has an error, then stop piping into it.
      // however, don't suppress the throwing behavior for this.
      function onerror(er) {
        unpipe();
        dest.removeListener('error', onerror);
        if (EE.listenerCount(dest, 'error') === 0)
          dest.emit('error', er);
      }
      // This is a brutally ugly hack to make sure that our error handler
      // is attached before any userland ones.  NEVER DO THIS.
      if (!dest._events || !dest._events.error)
        dest.on('error', onerror);
      else if (isArray(dest._events.error))
        dest._events.error.unshift(onerror);
      else
        dest._events.error = [onerror, dest._events.error];
    
    
    
      // Both close and finish should trigger unpipe, but only once.
      function onclose() {
        dest.removeListener('finish', onfinish);
        unpipe();
      }
      dest.once('close', onclose);
      function onfinish() {
        dest.removeListener('close', onclose);
        unpipe();
      }
      dest.once('finish', onfinish);
    
      function unpipe() {
        src.unpipe(dest);
      }
    
      // tell the dest that it's being piped to
      dest.emit('pipe', src);
    
      // start the flow if it hasn't been started already.
      if (!state.flowing) {
        // the handler that waits for readable events after all
        // the data gets sucked out in flow.
        // This would be easier to follow with a .once() handler
        // in flow(), but that is too slow.
        this.on('readable', pipeOnReadable);
    
        state.flowing = true;
        process.nextTick(function() {
          flow(src);
        });
      }
    
      return dest;
    };
    
    function pipeOnDrain(src) {
      return function() {
        var dest = this;
        var state = src._readableState;
        state.awaitDrain--;
        if (state.awaitDrain === 0)
          flow(src);
      };
    }
    
    function flow(src) {
      var state = src._readableState;
      var chunk;
      state.awaitDrain = 0;
    
      function write(dest, i, list) {
        var written = dest.write(chunk);
        if (false === written) {
          state.awaitDrain++;
        }
      }
    
      while (state.pipesCount && null !== (chunk = src.read())) {
    
        if (state.pipesCount === 1)
          write(state.pipes, 0, null);
        else
          forEach(state.pipes, write);
    
        src.emit('data', chunk);
    
        // if anyone needs a drain, then we have to wait for that.
        if (state.awaitDrain > 0)
          return;
      }
    
      // if every destination was unpiped, either before entering this
      // function, or in the while loop, then stop flowing.
      //
      // NB: This is a pretty rare edge case.
      if (state.pipesCount === 0) {
        state.flowing = false;
    
        // if there were data event listeners added, then switch to old mode.
        if (EE.listenerCount(src, 'data') > 0)
          emitDataEvents(src);
        return;
      }
    
      // at this point, no one needed a drain, so we just ran out of data
      // on the next readable event, start it over again.
      state.ranOut = true;
    }
    
    function pipeOnReadable() {
      if (this._readableState.ranOut) {
        this._readableState.ranOut = false;
        flow(this);
      }
    }
    
    
    Readable.prototype.unpipe = function(dest) {
      var state = this._readableState;
    
      // if we're not piping anywhere, then do nothing.
      if (state.pipesCount === 0)
        return this;
    
      // just one destination.  most common case.
      if (state.pipesCount === 1) {
        // passed in one, but it's not the right one.
        if (dest && dest !== state.pipes)
          return this;
    
        if (!dest)
          dest = state.pipes;
    
        // got a match.
        state.pipes = null;
        state.pipesCount = 0;
        this.removeListener('readable', pipeOnReadable);
        state.flowing = false;
        if (dest)
          dest.emit('unpipe', this);
        return this;
      }
    
      // slow case. multiple pipe destinations.
    
      if (!dest) {
        // remove all.
        var dests = state.pipes;
        var len = state.pipesCount;
        state.pipes = null;
        state.pipesCount = 0;
        this.removeListener('readable', pipeOnReadable);
        state.flowing = false;
    
        for (var i = 0; i < len; i++)
          dests[i].emit('unpipe', this);
        return this;
      }
    
      // try to find the right one.
      var i = indexOf(state.pipes, dest);
      if (i === -1)
        return this;
    
      state.pipes.splice(i, 1);
      state.pipesCount -= 1;
      if (state.pipesCount === 1)
        state.pipes = state.pipes[0];
    
      dest.emit('unpipe', this);
    
      return this;
    };
    
    // set up data events if they are asked for
    // Ensure readable listeners eventually get something
    Readable.prototype.on = function(ev, fn) {
      var res = Stream.prototype.on.call(this, ev, fn);
    
      if (ev === 'data' && !this._readableState.flowing)
        emitDataEvents(this);
    
      if (ev === 'readable' && this.readable) {
        var state = this._readableState;
        if (!state.readableListening) {
          state.readableListening = true;
          state.emittedReadable = false;
          state.needReadable = true;
          if (!state.reading) {
            this.read(0);
          } else if (state.length) {
            emitReadable(this, state);
          }
        }
      }
    
      return res;
    };
    Readable.prototype.addListener = Readable.prototype.on;
    
    // pause() and resume() are remnants of the legacy readable stream API
    // If the user uses them, then switch into old mode.
    Readable.prototype.resume = function() {
      emitDataEvents(this);
      this.read(0);
      this.emit('resume');
    };
    
    Readable.prototype.pause = function() {
      emitDataEvents(this, true);
      this.emit('pause');
    };
    
    function emitDataEvents(stream, startPaused) {
      var state = stream._readableState;
    
      if (state.flowing) {
        // https://github.com/isaacs/readable-stream/issues/16
        throw new Error('Cannot switch to old mode now.');
      }
    
      var paused = startPaused || false;
      var readable = false;
    
      // convert to an old-style stream.
      stream.readable = true;
      stream.pipe = Stream.prototype.pipe;
      stream.on = stream.addListener = Stream.prototype.on;
    
      stream.on('readable', function() {
        readable = true;
    
        var c;
        while (!paused && (null !== (c = stream.read())))
          stream.emit('data', c);
    
        if (c === null) {
          readable = false;
          stream._readableState.needReadable = true;
        }
      });
    
      stream.pause = function() {
        paused = true;
        this.emit('pause');
      };
    
      stream.resume = function() {
        paused = false;
        if (readable)
          process.nextTick(function() {
            stream.emit('readable');
          });
        else
          this.read(0);
        this.emit('resume');
      };
    
      // now make it start, just in case it hadn't already.
      stream.emit('readable');
    }
    
    // wrap an old-style stream as the async data source.
    // This is *not* part of the readable stream interface.
    // It is an ugly unfortunate mess of history.
    Readable.prototype.wrap = function(stream) {
      var state = this._readableState;
      var paused = false;
    
      var self = this;
      stream.on('end', function() {
        if (state.decoder && !state.ended) {
          var chunk = state.decoder.end();
          if (chunk && chunk.length)
            self.push(chunk);
        }
    
        self.push(null);
      });
    
      stream.on('data', function(chunk) {
        if (state.decoder)
          chunk = state.decoder.write(chunk);
    
        // don't skip over falsy values in objectMode
        //if (state.objectMode && util.isNullOrUndefined(chunk))
        if (state.objectMode && (chunk === null || chunk === undefined))
          return;
        else if (!state.objectMode && (!chunk || !chunk.length))
          return;
    
        var ret = self.push(chunk);
        if (!ret) {
          paused = true;
          stream.pause();
        }
      });
    
      // proxy all the other methods.
      // important when wrapping filters and duplexes.
      for (var i in stream) {
        if (typeof stream[i] === 'function' &&
            typeof this[i] === 'undefined') {
          this[i] = function(method) { return function() {
            return stream[method].apply(stream, arguments);
          }}(i);
        }
      }
    
      // proxy certain important events.
      var events = ['error', 'close', 'destroy', 'pause', 'resume'];
      forEach(events, function(ev) {
        stream.on(ev, self.emit.bind(self, ev));
      });
    
      // when we try to consume some more bytes, simply unpause the
      // underlying stream.
      self._read = function(n) {
        if (paused) {
          paused = false;
          stream.resume();
        }
      };
    
      return self;
    };
    
    
    
    // exposed for testing purposes only.
    Readable._fromList = fromList;
    
    // Pluck off n bytes from an array of buffers.
    // Length is the combined lengths of all the buffers in the list.
    function fromList(n, state) {
      var list = state.buffer;
      var length = state.length;
      var stringMode = !!state.decoder;
      var objectMode = !!state.objectMode;
      var ret;
    
      // nothing in the list, definitely empty.
      if (list.length === 0)
        return null;
    
      if (length === 0)
        ret = null;
      else if (objectMode)
        ret = list.shift();
      else if (!n || n >= length) {
        // read it all, truncate the array.
        if (stringMode)
          ret = list.join('');
        else
          ret = Buffer.concat(list, length);
        list.length = 0;
      } else {
        // read just some of it.
        if (n < list[0].length) {
          // just take a part of the first list item.
          // slice is the same for buffers and strings.
          var buf = list[0];
          ret = buf.slice(0, n);
          list[0] = buf.slice(n);
        } else if (n === list[0].length) {
          // first list is a perfect match
          ret = list.shift();
        } else {
          // complex case.
          // we have enough to cover it, but it spans past the first buffer.
          if (stringMode)
            ret = '';
          else
            ret = new Buffer(n);
    
          var c = 0;
          for (var i = 0, l = list.length; i < l && c < n; i++) {
            var buf = list[0];
            var cpy = Math.min(n - c, buf.length);
    
            if (stringMode)
              ret += buf.slice(0, cpy);
            else
              buf.copy(ret, c, 0, cpy);
    
            if (cpy < buf.length)
              list[0] = buf.slice(cpy);
            else
              list.shift();
    
            c += cpy;
          }
        }
      }
    
      return ret;
    }
    
    function endReadable(stream) {
      var state = stream._readableState;
    
      // If we get here before consuming all the bytes, then that is a
      // bug in node.  Should never happen.
      if (state.length > 0)
        throw new Error('endReadable called on non-empty stream');
    
      if (!state.endEmitted && state.calledRead) {
        state.ended = true;
        process.nextTick(function() {
          // Check that we didn't get one last unshift.
          if (!state.endEmitted && state.length === 0) {
            state.endEmitted = true;
            stream.readable = false;
            stream.emit('end');
          }
        });
      }
    }
    
    function forEach (xs, f) {
      for (var i = 0, l = xs.length; i < l; i++) {
        f(xs[i], i);
      }
    }
    
    function indexOf (xs, x) {
      for (var i = 0, l = xs.length; i < l; i++) {
        if (xs[i] === x) return i;
      }
      return -1;
    }
    
    }).call(this,require('_process'))
    },{"_process":22,"buffer":3,"core-util-is":29,"events":19,"inherits":20,"isarray":21,"stream":35,"string_decoder/":30}],27:[function(require,module,exports){
    // Copyright Joyent, Inc. and other Node contributors.
    //
    // Permission is hereby granted, free of charge, to any person obtaining a
    // copy of this software and associated documentation files (the
    // "Software"), to deal in the Software without restriction, including
    // without limitation the rights to use, copy, modify, merge, publish,
    // distribute, sublicense, and/or sell copies of the Software, and to permit
    // persons to whom the Software is furnished to do so, subject to the
    // following conditions:
    //
    // The above copyright notice and this permission notice shall be included
    // in all copies or substantial portions of the Software.
    //
    // THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS
    // OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF
    // MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN
    // NO EVENT SHALL THE AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM,
    // DAMAGES OR OTHER LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR
    // OTHERWISE, ARISING FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE
    // USE OR OTHER DEALINGS IN THE SOFTWARE.
    
    
    // a transform stream is a readable/writable stream where you do
    // something with the data.  Sometimes it's called a "filter",
    // but that's not a great name for it, since that implies a thing where
    // some bits pass through, and others are simply ignored.  (That would
    // be a valid example of a transform, of course.)
    //
    // While the output is causally related to the input, it's not a
    // necessarily symmetric or synchronous transformation.  For example,
    // a zlib stream might take multiple plain-text writes(), and then
    // emit a single compressed chunk some time in the future.
    //
    // Here's how this works:
    //
    // The Transform stream has all the aspects of the readable and writable
    // stream classes.  When you write(chunk), that calls _write(chunk,cb)
    // internally, and returns false if there's a lot of pending writes
    // buffered up.  When you call read(), that calls _read(n) until
    // there's enough pending readable data buffered up.
    //
    // In a transform stream, the written data is placed in a buffer.  When
    // _read(n) is called, it transforms the queued up data, calling the
    // buffered _write cb's as it consumes chunks.  If consuming a single
    // written chunk would result in multiple output chunks, then the first
    // outputted bit calls the readcb, and subsequent chunks just go into
    // the read buffer, and will cause it to emit 'readable' if necessary.
    //
    // This way, back-pressure is actually determined by the reading side,
    // since _read has to be called to start processing a new chunk.  However,
    // a pathological inflate type of transform can cause excessive buffering
    // here.  For example, imagine a stream where every byte of input is
    // interpreted as an integer from 0-255, and then results in that many
    // bytes of output.  Writing the 4 bytes {ff,ff,ff,ff} would result in
    // 1kb of data being output.  In this case, you could write a very small
    // amount of input, and end up with a very large amount of output.  In
    // such a pathological inflating mechanism, there'd be no way to tell
    // the system to stop doing the transform.  A single 4MB write could
    // cause the system to run out of memory.
    //
    // However, even in such a pathological case, only a single written chunk
    // would be consumed, and then the rest would wait (un-transformed) until
    // the results of the previous transformed chunk were consumed.
    
    module.exports = Transform;
    
    var Duplex = require('./_stream_duplex');
    
    /*<replacement>*/
    var util = require('core-util-is');
    util.inherits = require('inherits');
    /*</replacement>*/
    
    util.inherits(Transform, Duplex);
    
    
    function TransformState(options, stream) {
      this.afterTransform = function(er, data) {
        return afterTransform(stream, er, data);
      };
    
      this.needTransform = false;
      this.transforming = false;
      this.writecb = null;
      this.writechunk = null;
    }
    
    function afterTransform(stream, er, data) {
      var ts = stream._transformState;
      ts.transforming = false;
    
      var cb = ts.writecb;
    
      if (!cb)
        return stream.emit('error', new Error('no writecb in Transform class'));
    
      ts.writechunk = null;
      ts.writecb = null;
    
      if (data !== null && data !== undefined)
        stream.push(data);
    
      if (cb)
        cb(er);
    
      var rs = stream._readableState;
      rs.reading = false;
      if (rs.needReadable || rs.length < rs.highWaterMark) {
        stream._read(rs.highWaterMark);
      }
    }
    
    
    function Transform(options) {
      if (!(this instanceof Transform))
        return new Transform(options);
    
      Duplex.call(this, options);
    
      var ts = this._transformState = new TransformState(options, this);
    
      // when the writable side finishes, then flush out anything remaining.
      var stream = this;
    
      // start out asking for a readable event once data is transformed.
      this._readableState.needReadable = true;
    
      // we have implemented the _read method, and done the other things
      // that Readable wants before the first _read call, so unset the
      // sync guard flag.
      this._readableState.sync = false;
    
      this.once('finish', function() {
        if ('function' === typeof this._flush)
          this._flush(function(er) {
            done(stream, er);
          });
        else
          done(stream);
      });
    }
    
    Transform.prototype.push = function(chunk, encoding) {
      this._transformState.needTransform = false;
      return Duplex.prototype.push.call(this, chunk, encoding);
    };
    
    // This is the part where you do stuff!
    // override this function in implementation classes.
    // 'chunk' is an input chunk.
    //
    // Call `push(newChunk)` to pass along transformed output
    // to the readable side.  You may call 'push' zero or more times.
    //
    // Call `cb(err)` when you are done with this chunk.  If you pass
    // an error, then that'll put the hurt on the whole operation.  If you
    // never call cb(), then you'll never get another chunk.
    Transform.prototype._transform = function(chunk, encoding, cb) {
      throw new Error('not implemented');
    };
    
    Transform.prototype._write = function(chunk, encoding, cb) {
      var ts = this._transformState;
      ts.writecb = cb;
      ts.writechunk = chunk;
      ts.writeencoding = encoding;
      if (!ts.transforming) {
        var rs = this._readableState;
        if (ts.needTransform ||
            rs.needReadable ||
            rs.length < rs.highWaterMark)
          this._read(rs.highWaterMark);
      }
    };
    
    // Doesn't matter what the args are here.
    // _transform does all the work.
    // That we got here means that the readable side wants more data.
    Transform.prototype._read = function(n) {
      var ts = this._transformState;
    
      if (ts.writechunk !== null && ts.writecb && !ts.transforming) {
        ts.transforming = true;
        this._transform(ts.writechunk, ts.writeencoding, ts.afterTransform);
      } else {
        // mark that we need a transform, so that any data that comes in
        // will get processed, now that we've asked for it.
        ts.needTransform = true;
      }
    };
    
    
    function done(stream, er) {
      if (er)
        return stream.emit('error', er);
    
      // if there's nothing in the write buffer, then that means
      // that nothing more will ever be provided
      var ws = stream._writableState;
      var rs = stream._readableState;
      var ts = stream._transformState;
    
      if (ws.length)
        throw new Error('calling transform done when ws.length != 0');
    
      if (ts.transforming)
        throw new Error('calling transform done when still transforming');
    
      return stream.push(null);
    }
    
    },{"./_stream_duplex":24,"core-util-is":29,"inherits":20}],28:[function(require,module,exports){
    (function (process){
    // Copyright Joyent, Inc. and other Node contributors.
    //
    // Permission is hereby granted, free of charge, to any person obtaining a
    // copy of this software and associated documentation files (the
    // "Software"), to deal in the Software without restriction, including
    // without limitation the rights to use, copy, modify, merge, publish,
    // distribute, sublicense, and/or sell copies of the Software, and to permit
    // persons to whom the Software is furnished to do so, subject to the
    // following conditions:
    //
    // The above copyright notice and this permission notice shall be included
    // in all copies or substantial portions of the Software.
    //
    // THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS
    // OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF
    // MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN
    // NO EVENT SHALL THE AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM,
    // DAMAGES OR OTHER LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR
    // OTHERWISE, ARISING FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE
    // USE OR OTHER DEALINGS IN THE SOFTWARE.
    
    // A bit simpler than readable streams.
    // Implement an async ._write(chunk, cb), and it'll handle all
    // the drain event emission and buffering.
    
    module.exports = Writable;
    
    /*<replacement>*/
    var Buffer = require('buffer').Buffer;
    /*</replacement>*/
    
    Writable.WritableState = WritableState;
    
    
    /*<replacement>*/
    var util = require('core-util-is');
    util.inherits = require('inherits');
    /*</replacement>*/
    
    var Stream = require('stream');
    
    util.inherits(Writable, Stream);
    
    function WriteReq(chunk, encoding, cb) {
      this.chunk = chunk;
      this.encoding = encoding;
      this.callback = cb;
    }
    
    function WritableState(options, stream) {
      options = options || {};
    
      // the point at which write() starts returning false
      // Note: 0 is a valid value, means that we always return false if
      // the entire buffer is not flushed immediately on write()
      var hwm = options.highWaterMark;
      this.highWaterMark = (hwm || hwm === 0) ? hwm : 16 * 1024;
    
      // object stream flag to indicate whether or not this stream
      // contains buffers or objects.
      this.objectMode = !!options.objectMode;
    
      // cast to ints.
      this.highWaterMark = ~~this.highWaterMark;
    
      this.needDrain = false;
      // at the start of calling end()
      this.ending = false;
      // when end() has been called, and returned
      this.ended = false;
      // when 'finish' is emitted
      this.finished = false;
    
      // should we decode strings into buffers before passing to _write?
      // this is here so that some node-core streams can optimize string
      // handling at a lower level.
      var noDecode = options.decodeStrings === false;
      this.decodeStrings = !noDecode;
    
      // Crypto is kind of old and crusty.  Historically, its default string
      // encoding is 'binary' so we have to make this configurable.
      // Everything else in the universe uses 'utf8', though.
      this.defaultEncoding = options.defaultEncoding || 'utf8';
    
      // not an actual buffer we keep track of, but a measurement
      // of how much we're waiting to get pushed to some underlying
      // socket or file.
      this.length = 0;
    
      // a flag to see when we're in the middle of a write.
      this.writing = false;
    
      // a flag to be able to tell if the onwrite cb is called immediately,
      // or on a later tick.  We set this to true at first, becuase any
      // actions that shouldn't happen until "later" should generally also
      // not happen before the first write call.
      this.sync = true;
    
      // a flag to know if we're processing previously buffered items, which
      // may call the _write() callback in the same tick, so that we don't
      // end up in an overlapped onwrite situation.
      this.bufferProcessing = false;
    
      // the callback that's passed to _write(chunk,cb)
      this.onwrite = function(er) {
        onwrite(stream, er);
      };
    
      // the callback that the user supplies to write(chunk,encoding,cb)
      this.writecb = null;
    
      // the amount that is being written when _write is called.
      this.writelen = 0;
    
      this.buffer = [];
    
      // True if the error was already emitted and should not be thrown again
      this.errorEmitted = false;
    }
    
    function Writable(options) {
      var Duplex = require('./_stream_duplex');
    
      // Writable ctor is applied to Duplexes, though they're not
      // instanceof Writable, they're instanceof Readable.
      if (!(this instanceof Writable) && !(this instanceof Duplex))
        return new Writable(options);
    
      this._writableState = new WritableState(options, this);
    
      // legacy.
      this.writable = true;
    
      Stream.call(this);
    }
    
    // Otherwise people can pipe Writable streams, which is just wrong.
    Writable.prototype.pipe = function() {
      this.emit('error', new Error('Cannot pipe. Not readable.'));
    };
    
    
    function writeAfterEnd(stream, state, cb) {
      var er = new Error('write after end');
      // TODO: defer error events consistently everywhere, not just the cb
      stream.emit('error', er);
      process.nextTick(function() {
        cb(er);
      });
    }
    
    // If we get something that is not a buffer, string, null, or undefined,
    // and we're not in objectMode, then that's an error.
    // Otherwise stream chunks are all considered to be of length=1, and the
    // watermarks determine how many objects to keep in the buffer, rather than
    // how many bytes or characters.
    function validChunk(stream, state, chunk, cb) {
      var valid = true;
      if (!Buffer.isBuffer(chunk) &&
          'string' !== typeof chunk &&
          chunk !== null &&
          chunk !== undefined &&
          !state.objectMode) {
        var er = new TypeError('Invalid non-string/buffer chunk');
        stream.emit('error', er);
        process.nextTick(function() {
          cb(er);
        });
        valid = false;
      }
      return valid;
    }
    
    Writable.prototype.write = function(chunk, encoding, cb) {
      var state = this._writableState;
      var ret = false;
    
      if (typeof encoding === 'function') {
        cb = encoding;
        encoding = null;
      }
    
      if (Buffer.isBuffer(chunk))
        encoding = 'buffer';
      else if (!encoding)
        encoding = state.defaultEncoding;
    
      if (typeof cb !== 'function')
        cb = function() {};
    
      if (state.ended)
        writeAfterEnd(this, state, cb);
      else if (validChunk(this, state, chunk, cb))
        ret = writeOrBuffer(this, state, chunk, encoding, cb);
    
      return ret;
    };
    
    function decodeChunk(state, chunk, encoding) {
      if (!state.objectMode &&
          state.decodeStrings !== false &&
          typeof chunk === 'string') {
        chunk = new Buffer(chunk, encoding);
      }
      return chunk;
    }
    
    // if we're already writing something, then just put this
    // in the queue, and wait our turn.  Otherwise, call _write
    // If we return false, then we need a drain event, so set that flag.
    function writeOrBuffer(stream, state, chunk, encoding, cb) {
      chunk = decodeChunk(state, chunk, encoding);
      if (Buffer.isBuffer(chunk))
        encoding = 'buffer';
      var len = state.objectMode ? 1 : chunk.length;
    
      state.length += len;
    
      var ret = state.length < state.highWaterMark;
      // we must ensure that previous needDrain will not be reset to false.
      if (!ret)
        state.needDrain = true;
    
      if (state.writing)
        state.buffer.push(new WriteReq(chunk, encoding, cb));
      else
        doWrite(stream, state, len, chunk, encoding, cb);
    
      return ret;
    }
    
    function doWrite(stream, state, len, chunk, encoding, cb) {
      state.writelen = len;
      state.writecb = cb;
      state.writing = true;
      state.sync = true;
      stream._write(chunk, encoding, state.onwrite);
      state.sync = false;
    }
    
    function onwriteError(stream, state, sync, er, cb) {
      if (sync)
        process.nextTick(function() {
          cb(er);
        });
      else
        cb(er);
    
      stream._writableState.errorEmitted = true;
      stream.emit('error', er);
    }
    
    function onwriteStateUpdate(state) {
      state.writing = false;
      state.writecb = null;
      state.length -= state.writelen;
      state.writelen = 0;
    }
    
    function onwrite(stream, er) {
      var state = stream._writableState;
      var sync = state.sync;
      var cb = state.writecb;
    
      onwriteStateUpdate(state);
    
      if (er)
        onwriteError(stream, state, sync, er, cb);
      else {
        // Check if we're actually ready to finish, but don't emit yet
        var finished = needFinish(stream, state);
    
        if (!finished && !state.bufferProcessing && state.buffer.length)
          clearBuffer(stream, state);
    
        if (sync) {
          process.nextTick(function() {
            afterWrite(stream, state, finished, cb);
          });
        } else {
          afterWrite(stream, state, finished, cb);
        }
      }
    }
    
    function afterWrite(stream, state, finished, cb) {
      if (!finished)
        onwriteDrain(stream, state);
      cb();
      if (finished)
        finishMaybe(stream, state);
    }
    
    // Must force callback to be called on nextTick, so that we don't
    // emit 'drain' before the write() consumer gets the 'false' return
    // value, and has a chance to attach a 'drain' listener.
    function onwriteDrain(stream, state) {
      if (state.length === 0 && state.needDrain) {
        state.needDrain = false;
        stream.emit('drain');
      }
    }
    
    
    // if there's something in the buffer waiting, then process it
    function clearBuffer(stream, state) {
      state.bufferProcessing = true;
    
      for (var c = 0; c < state.buffer.length; c++) {
        var entry = state.buffer[c];
        var chunk = entry.chunk;
        var encoding = entry.encoding;
        var cb = entry.callback;
        var len = state.objectMode ? 1 : chunk.length;
    
        doWrite(stream, state, len, chunk, encoding, cb);
    
        // if we didn't call the onwrite immediately, then
        // it means that we need to wait until it does.
        // also, that means that the chunk and cb are currently
        // being processed, so move the buffer counter past them.
        if (state.writing) {
          c++;
          break;
        }
      }
    
      state.bufferProcessing = false;
      if (c < state.buffer.length)
        state.buffer = state.buffer.slice(c);
      else
        state.buffer.length = 0;
    }
    
    Writable.prototype._write = function(chunk, encoding, cb) {
      cb(new Error('not implemented'));
    };
    
    Writable.prototype.end = function(chunk, encoding, cb) {
      var state = this._writableState;
    
      if (typeof chunk === 'function') {
        cb = chunk;
        chunk = null;
        encoding = null;
      } else if (typeof encoding === 'function') {
        cb = encoding;
        encoding = null;
      }
    
      if (typeof chunk !== 'undefined' && chunk !== null)
        this.write(chunk, encoding);
    
      // ignore unnecessary end() calls.
      if (!state.ending && !state.finished)
        endWritable(this, state, cb);
    };
    
    
    function needFinish(stream, state) {
      return (state.ending &&
              state.length === 0 &&
              !state.finished &&
              !state.writing);
    }
    
    function finishMaybe(stream, state) {
      var need = needFinish(stream, state);
      if (need) {
        state.finished = true;
        stream.emit('finish');
      }
      return need;
    }
    
    function endWritable(stream, state, cb) {
      state.ending = true;
      finishMaybe(stream, state);
      if (cb) {
        if (state.finished)
          process.nextTick(cb);
        else
          stream.once('finish', cb);
      }
      state.ended = true;
    }
    
    }).call(this,require('_process'))
    },{"./_stream_duplex":24,"_process":22,"buffer":3,"core-util-is":29,"inherits":20,"stream":35}],29:[function(require,module,exports){
    (function (Buffer){
    // Copyright Joyent, Inc. and other Node contributors.
    //
    // Permission is hereby granted, free of charge, to any person obtaining a
    // copy of this software and associated documentation files (the
    // "Software"), to deal in the Software without restriction, including
    // without limitation the rights to use, copy, modify, merge, publish,
    // distribute, sublicense, and/or sell copies of the Software, and to permit
    // persons to whom the Software is furnished to do so, subject to the
    // following conditions:
    //
    // The above copyright notice and this permission notice shall be included
    // in all copies or substantial portions of the Software.
    //
    // THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS
    // OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF
    // MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN
    // NO EVENT SHALL THE AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM,
    // DAMAGES OR OTHER LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR
    // OTHERWISE, ARISING FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE
    // USE OR OTHER DEALINGS IN THE SOFTWARE.
    
    // NOTE: These type checking functions intentionally don't use `instanceof`
    // because it is fragile and can be easily faked with `Object.create()`.
    function isArray(ar) {
      return Array.isArray(ar);
    }
    exports.isArray = isArray;
    
    function isBoolean(arg) {
      return typeof arg === 'boolean';
    }
    exports.isBoolean = isBoolean;
    
    function isNull(arg) {
      return arg === null;
    }
    exports.isNull = isNull;
    
    function isNullOrUndefined(arg) {
      return arg == null;
    }
    exports.isNullOrUndefined = isNullOrUndefined;
    
    function isNumber(arg) {
      return typeof arg === 'number';
    }
    exports.isNumber = isNumber;
    
    function isString(arg) {
      return typeof arg === 'string';
    }
    exports.isString = isString;
    
    function isSymbol(arg) {
      return typeof arg === 'symbol';
    }
    exports.isSymbol = isSymbol;
    
    function isUndefined(arg) {
      return arg === void 0;
    }
    exports.isUndefined = isUndefined;
    
    function isRegExp(re) {
      return isObject(re) && objectToString(re) === '[object RegExp]';
    }
    exports.isRegExp = isRegExp;
    
    function isObject(arg) {
      return typeof arg === 'object' && arg !== null;
    }
    exports.isObject = isObject;
    
    function isDate(d) {
      return isObject(d) && objectToString(d) === '[object Date]';
    }
    exports.isDate = isDate;
    
    function isError(e) {
      return isObject(e) &&
          (objectToString(e) === '[object Error]' || e instanceof Error);
    }
    exports.isError = isError;
    
    function isFunction(arg) {
      return typeof arg === 'function';
    }
    exports.isFunction = isFunction;
    
    function isPrimitive(arg) {
      return arg === null ||
             typeof arg === 'boolean' ||
             typeof arg === 'number' ||
             typeof arg === 'string' ||
             typeof arg === 'symbol' ||  // ES6 symbol
             typeof arg === 'undefined';
    }
    exports.isPrimitive = isPrimitive;
    
    function isBuffer(arg) {
      return Buffer.isBuffer(arg);
    }
    exports.isBuffer = isBuffer;
    
    function objectToString(o) {
      return Object.prototype.toString.call(o);
    }
    }).call(this,require("buffer").Buffer)
    },{"buffer":3}],30:[function(require,module,exports){
    // Copyright Joyent, Inc. and other Node contributors.
    //
    // Permission is hereby granted, free of charge, to any person obtaining a
    // copy of this software and associated documentation files (the
    // "Software"), to deal in the Software without restriction, including
    // without limitation the rights to use, copy, modify, merge, publish,
    // distribute, sublicense, and/or sell copies of the Software, and to permit
    // persons to whom the Software is furnished to do so, subject to the
    // following conditions:
    //
    // The above copyright notice and this permission notice shall be included
    // in all copies or substantial portions of the Software.
    //
    // THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS
    // OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF
    // MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN
    // NO EVENT SHALL THE AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM,
    // DAMAGES OR OTHER LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR
    // OTHERWISE, ARISING FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE
    // USE OR OTHER DEALINGS IN THE SOFTWARE.
    
    var Buffer = require('buffer').Buffer;
    
    var isBufferEncoding = Buffer.isEncoding
      || function(encoding) {
           switch (encoding && encoding.toLowerCase()) {
             case 'hex': case 'utf8': case 'utf-8': case 'ascii': case 'binary': case 'base64': case 'ucs2': case 'ucs-2': case 'utf16le': case 'utf-16le': case 'raw': return true;
             default: return false;
           }
         }
    
    
    function assertEncoding(encoding) {
      if (encoding && !isBufferEncoding(encoding)) {
        throw new Error('Unknown encoding: ' + encoding);
      }
    }
    
    // StringDecoder provides an interface for efficiently splitting a series of
    // buffers into a series of JS strings without breaking apart multi-byte
    // characters. CESU-8 is handled as part of the UTF-8 encoding.
    //
    // @TODO Handling all encodings inside a single object makes it very difficult
    // to reason about this code, so it should be split up in the future.
    // @TODO There should be a utf8-strict encoding that rejects invalid UTF-8 code
    // points as used by CESU-8.
    var StringDecoder = exports.StringDecoder = function(encoding) {
      this.encoding = (encoding || 'utf8').toLowerCase().replace(/[-_]/, '');
      assertEncoding(encoding);
      switch (this.encoding) {
        case 'utf8':
          // CESU-8 represents each of Surrogate Pair by 3-bytes
          this.surrogateSize = 3;
          break;
        case 'ucs2':
        case 'utf16le':
          // UTF-16 represents each of Surrogate Pair by 2-bytes
          this.surrogateSize = 2;
          this.detectIncompleteChar = utf16DetectIncompleteChar;
          break;
        case 'base64':
          // Base-64 stores 3 bytes in 4 chars, and pads the remainder.
          this.surrogateSize = 3;
          this.detectIncompleteChar = base64DetectIncompleteChar;
          break;
        default:
          this.write = passThroughWrite;
          return;
      }
    
      // Enough space to store all bytes of a single character. UTF-8 needs 4
      // bytes, but CESU-8 may require up to 6 (3 bytes per surrogate).
      this.charBuffer = new Buffer(6);
      // Number of bytes received for the current incomplete multi-byte character.
      this.charReceived = 0;
      // Number of bytes expected for the current incomplete multi-byte character.
      this.charLength = 0;
    };
    
    
    // write decodes the given buffer and returns it as JS string that is
    // guaranteed to not contain any partial multi-byte characters. Any partial
    // character found at the end of the buffer is buffered up, and will be
    // returned when calling write again with the remaining bytes.
    //
    // Note: Converting a Buffer containing an orphan surrogate to a String
    // currently works, but converting a String to a Buffer (via `new Buffer`, or
    // Buffer#write) will replace incomplete surrogates with the unicode
    // replacement character. See https://codereview.chromium.org/121173009/ .
    StringDecoder.prototype.write = function(buffer) {
      var charStr = '';
      // if our last write ended with an incomplete multibyte character
      while (this.charLength) {
        // determine how many remaining bytes this buffer has to offer for this char
        var available = (buffer.length >= this.charLength - this.charReceived) ?
            this.charLength - this.charReceived :
            buffer.length;
    
        // add the new bytes to the char buffer
        buffer.copy(this.charBuffer, this.charReceived, 0, available);
        this.charReceived += available;
    
        if (this.charReceived < this.charLength) {
          // still not enough chars in this buffer? wait for more ...
          return '';
        }
    
        // remove bytes belonging to the current character from the buffer
        buffer = buffer.slice(available, buffer.length);
    
        // get the character that was split
        charStr = this.charBuffer.slice(0, this.charLength).toString(this.encoding);
    
        // CESU-8: lead surrogate (D800-DBFF) is also the incomplete character
        var charCode = charStr.charCodeAt(charStr.length - 1);
        if (charCode >= 0xD800 && charCode <= 0xDBFF) {
          this.charLength += this.surrogateSize;
          charStr = '';
          continue;
        }
        this.charReceived = this.charLength = 0;
    
        // if there are no more bytes in this buffer, just emit our char
        if (buffer.length === 0) {
          return charStr;
        }
        break;
      }
    
      // determine and set charLength / charReceived
      this.detectIncompleteChar(buffer);
    
      var end = buffer.length;
      if (this.charLength) {
        // buffer the incomplete character bytes we got
        buffer.copy(this.charBuffer, 0, buffer.length - this.charReceived, end);
        end -= this.charReceived;
      }
    
      charStr += buffer.toString(this.encoding, 0, end);
    
      var end = charStr.length - 1;
      var charCode = charStr.charCodeAt(end);
      // CESU-8: lead surrogate (D800-DBFF) is also the incomplete character
      if (charCode >= 0xD800 && charCode <= 0xDBFF) {
        var size = this.surrogateSize;
        this.charLength += size;
        this.charReceived += size;
        this.charBuffer.copy(this.charBuffer, size, 0, size);
        buffer.copy(this.charBuffer, 0, 0, size);
        return charStr.substring(0, end);
      }
    
      // or just emit the charStr
      return charStr;
    };
    
    // detectIncompleteChar determines if there is an incomplete UTF-8 character at
    // the end of the given buffer. If so, it sets this.charLength to the byte
    // length that character, and sets this.charReceived to the number of bytes
    // that are available for this character.
    StringDecoder.prototype.detectIncompleteChar = function(buffer) {
      // determine how many bytes we have to check at the end of this buffer
      var i = (buffer.length >= 3) ? 3 : buffer.length;
    
      // Figure out if one of the last i bytes of our buffer announces an
      // incomplete char.
      for (; i > 0; i--) {
        var c = buffer[buffer.length - i];
    
        // See http://en.wikipedia.org/wiki/UTF-8#Description
    
        // 110XXXXX
        if (i == 1 && c >> 5 == 0x06) {
          this.charLength = 2;
          break;
        }
    
        // 1110XXXX
        if (i <= 2 && c >> 4 == 0x0E) {
          this.charLength = 3;
          break;
        }
    
        // 11110XXX
        if (i <= 3 && c >> 3 == 0x1E) {
          this.charLength = 4;
          break;
        }
      }
      this.charReceived = i;
    };
    
    StringDecoder.prototype.end = function(buffer) {
      var res = '';
      if (buffer && buffer.length)
        res = this.write(buffer);
    
      if (this.charReceived) {
        var cr = this.charReceived;
        var buf = this.charBuffer;
        var enc = this.encoding;
        res += buf.slice(0, cr).toString(enc);
      }
    
      return res;
    };
    
    function passThroughWrite(buffer) {
      return buffer.toString(this.encoding);
    }
    
    function utf16DetectIncompleteChar(buffer) {
      this.charReceived = buffer.length % 2;
      this.charLength = this.charReceived ? 2 : 0;
    }
    
    function base64DetectIncompleteChar(buffer) {
      this.charReceived = buffer.length % 3;
      this.charLength = this.charReceived ? 3 : 0;
    }
    
    },{"buffer":3}],31:[function(require,module,exports){
    module.exports = require("./lib/_stream_passthrough.js")
    
    },{"./lib/_stream_passthrough.js":25}],32:[function(require,module,exports){
    exports = module.exports = require('./lib/_stream_readable.js');
    exports.Readable = exports;
    exports.Writable = require('./lib/_stream_writable.js');
    exports.Duplex = require('./lib/_stream_duplex.js');
    exports.Transform = require('./lib/_stream_transform.js');
    exports.PassThrough = require('./lib/_stream_passthrough.js');
    
    },{"./lib/_stream_duplex.js":24,"./lib/_stream_passthrough.js":25,"./lib/_stream_readable.js":26,"./lib/_stream_transform.js":27,"./lib/_stream_writable.js":28}],33:[function(require,module,exports){
    module.exports = require("./lib/_stream_transform.js")
    
    },{"./lib/_stream_transform.js":27}],34:[function(require,module,exports){
    module.exports = require("./lib/_stream_writable.js")
    
    },{"./lib/_stream_writable.js":28}],35:[function(require,module,exports){
    // Copyright Joyent, Inc. and other Node contributors.
    //
    // Permission is hereby granted, free of charge, to any person obtaining a
    // copy of this software and associated documentation files (the
    // "Software"), to deal in the Software without restriction, including
    // without limitation the rights to use, copy, modify, merge, publish,
    // distribute, sublicense, and/or sell copies of the Software, and to permit
    // persons to whom the Software is furnished to do so, subject to the
    // following conditions:
    //
    // The above copyright notice and this permission notice shall be included
    // in all copies or substantial portions of the Software.
    //
    // THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS
    // OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF
    // MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN
    // NO EVENT SHALL THE AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM,
    // DAMAGES OR OTHER LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR
    // OTHERWISE, ARISING FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE
    // USE OR OTHER DEALINGS IN THE SOFTWARE.
    
    module.exports = Stream;
    
    var EE = require('events').EventEmitter;
    var inherits = require('inherits');
    
    inherits(Stream, EE);
    Stream.Readable = require('readable-stream/readable.js');
    Stream.Writable = require('readable-stream/writable.js');
    Stream.Duplex = require('readable-stream/duplex.js');
    Stream.Transform = require('readable-stream/transform.js');
    Stream.PassThrough = require('readable-stream/passthrough.js');
    
    // Backwards-compat with node 0.4.x
    Stream.Stream = Stream;
    
    
    
    // old-style streams.  Note that the pipe method (the only relevant
    // part of this class) is overridden in the Readable class.
    
    function Stream() {
      EE.call(this);
    }
    
    Stream.prototype.pipe = function(dest, options) {
      var source = this;
    
      function ondata(chunk) {
        if (dest.writable) {
          if (false === dest.write(chunk) && source.pause) {
            source.pause();
          }
        }
      }
    
      source.on('data', ondata);
    
      function ondrain() {
        if (source.readable && source.resume) {
          source.resume();
        }
      }
    
      dest.on('drain', ondrain);
    
      // If the 'end' option is not supplied, dest.end() will be called when
      // source gets the 'end' or 'close' events.  Only dest.end() once.
      if (!dest._isStdio && (!options || options.end !== false)) {
        source.on('end', onend);
        source.on('close', onclose);
      }
    
      var didOnEnd = false;
      function onend() {
        if (didOnEnd) return;
        didOnEnd = true;
    
        dest.end();
      }
    
    
      function onclose() {
        if (didOnEnd) return;
        didOnEnd = true;
    
        if (typeof dest.destroy === 'function') dest.destroy();
      }
    
      // don't leave dangling pipes when there are errors.
      function onerror(er) {
        cleanup();
        if (EE.listenerCount(this, 'error') === 0) {
          throw er; // Unhandled stream error in pipe.
        }
      }
    
      source.on('error', onerror);
      dest.on('error', onerror);
    
      // remove all the event listeners that were added.
      function cleanup() {
        source.removeListener('data', ondata);
        dest.removeListener('drain', ondrain);
    
        source.removeListener('end', onend);
        source.removeListener('close', onclose);
    
        source.removeListener('error', onerror);
        dest.removeListener('error', onerror);
    
        source.removeListener('end', cleanup);
        source.removeListener('close', cleanup);
    
        dest.removeListener('close', cleanup);
      }
    
      source.on('end', cleanup);
      source.on('close', cleanup);
    
      dest.on('close', cleanup);
    
      dest.emit('pipe', source);
    
      // Allow for unix-like usage: A.pipe(B).pipe(C)
      return dest;
    };
    
    },{"events":19,"inherits":20,"readable-stream/duplex.js":23,"readable-stream/passthrough.js":31,"readable-stream/readable.js":32,"readable-stream/transform.js":33,"readable-stream/writable.js":34}],36:[function(require,module,exports){
    module.exports = function isBuffer(arg) {
      return arg && typeof arg === 'object'
        && typeof arg.copy === 'function'
        && typeof arg.fill === 'function'
        && typeof arg.readUInt8 === 'function';
    }
    },{}],37:[function(require,module,exports){
    (function (process,global){
    // Copyright Joyent, Inc. and other Node contributors.
    //
    // Permission is hereby granted, free of charge, to any person obtaining a
    // copy of this software and associated documentation files (the
    // "Software"), to deal in the Software without restriction, including
    // without limitation the rights to use, copy, modify, merge, publish,
    // distribute, sublicense, and/or sell copies of the Software, and to permit
    // persons to whom the Software is furnished to do so, subject to the
    // following conditions:
    //
    // The above copyright notice and this permission notice shall be included
    // in all copies or substantial portions of the Software.
    //
    // THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS
    // OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF
    // MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN
    // NO EVENT SHALL THE AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM,
    // DAMAGES OR OTHER LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR
    // OTHERWISE, ARISING FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE
    // USE OR OTHER DEALINGS IN THE SOFTWARE.
    
    var formatRegExp = /%[sdj%]/g;
    exports.format = function(f) {
      if (!isString(f)) {
        var objects = [];
        for (var i = 0; i < arguments.length; i++) {
          objects.push(inspect(arguments[i]));
        }
        return objects.join(' ');
      }
    
      var i = 1;
      var args = arguments;
      var len = args.length;
      var str = String(f).replace(formatRegExp, function(x) {
        if (x === '%%') return '%';
        if (i >= len) return x;
        switch (x) {
          case '%s': return String(args[i++]);
          case '%d': return Number(args[i++]);
          case '%j':
            try {
              return JSON.stringify(args[i++]);
            } catch (_) {
              return '[Circular]';
            }
          default:
            return x;
        }
      });
      for (var x = args[i]; i < len; x = args[++i]) {
        if (isNull(x) || !isObject(x)) {
          str += ' ' + x;
        } else {
          str += ' ' + inspect(x);
        }
      }
      return str;
    };
    
    
    // Mark that a method should not be used.
    // Returns a modified function which warns once by default.
    // If --no-deprecation is set, then it is a no-op.
    exports.deprecate = function(fn, msg) {
      // Allow for deprecating things in the process of starting up.
      if (isUndefined(global.process)) {
        return function() {
          return exports.deprecate(fn, msg).apply(this, arguments);
        };
      }
    
      if (process.noDeprecation === true) {
        return fn;
      }
    
      var warned = false;
      function deprecated() {
        if (!warned) {
          if (process.throwDeprecation) {
            throw new Error(msg);
          } else if (process.traceDeprecation) {
            console.trace(msg);
          } else {
            console.error(msg);
          }
          warned = true;
        }
        return fn.apply(this, arguments);
      }
    
      return deprecated;
    };
    
    
    var debugs = {};
    var debugEnviron;
    exports.debuglog = function(set) {
      if (isUndefined(debugEnviron))
        debugEnviron = process.env.NODE_DEBUG || '';
      set = set.toUpperCase();
      if (!debugs[set]) {
        if (new RegExp('\\b' + set + '\\b', 'i').test(debugEnviron)) {
          var pid = process.pid;
          debugs[set] = function() {
            var msg = exports.format.apply(exports, arguments);
            console.error('%s %d: %s', set, pid, msg);
          };
        } else {
          debugs[set] = function() {};
        }
      }
      return debugs[set];
    };
    
    
    /**
     * Echos the value of a value. Trys to print the value out
     * in the best way possible given the different types.
     *
     * @param {Object} obj The object to print out.
     * @param {Object} opts Optional options object that alters the output.
     */
    /* legacy: obj, showHidden, depth, colors*/
    function inspect(obj, opts) {
      // default options
      var ctx = {
        seen: [],
        stylize: stylizeNoColor
      };
      // legacy...
      if (arguments.length >= 3) ctx.depth = arguments[2];
      if (arguments.length >= 4) ctx.colors = arguments[3];
      if (isBoolean(opts)) {
        // legacy...
        ctx.showHidden = opts;
      } else if (opts) {
        // got an "options" object
        exports._extend(ctx, opts);
      }
      // set default options
      if (isUndefined(ctx.showHidden)) ctx.showHidden = false;
      if (isUndefined(ctx.depth)) ctx.depth = 2;
      if (isUndefined(ctx.colors)) ctx.colors = false;
      if (isUndefined(ctx.customInspect)) ctx.customInspect = true;
      if (ctx.colors) ctx.stylize = stylizeWithColor;
      return formatValue(ctx, obj, ctx.depth);
    }
    exports.inspect = inspect;
    
    
    // http://en.wikipedia.org/wiki/ANSI_escape_code#graphics
    inspect.colors = {
      'bold' : [1, 22],
      'italic' : [3, 23],
      'underline' : [4, 24],
      'inverse' : [7, 27],
      'white' : [37, 39],
      'grey' : [90, 39],
      'black' : [30, 39],
      'blue' : [34, 39],
      'cyan' : [36, 39],
      'green' : [32, 39],
      'magenta' : [35, 39],
      'red' : [31, 39],
      'yellow' : [33, 39]
    };
    
    // Don't use 'blue' not visible on cmd.exe
    inspect.styles = {
      'special': 'cyan',
      'number': 'yellow',
      'boolean': 'yellow',
      'undefined': 'grey',
      'null': 'bold',
      'string': 'green',
      'date': 'magenta',
      // "name": intentionally not styling
      'regexp': 'red'
    };
    
    
    function stylizeWithColor(str, styleType) {
      var style = inspect.styles[styleType];
    
      if (style) {
        return '\u001b[' + inspect.colors[style][0] + 'm' + str +
               '\u001b[' + inspect.colors[style][1] + 'm';
      } else {
        return str;
      }
    }
    
    
    function stylizeNoColor(str, styleType) {
      return str;
    }
    
    
    function arrayToHash(array) {
      var hash = {};
    
      array.forEach(function(val, idx) {
        hash[val] = true;
      });
    
      return hash;
    }
    
    
    function formatValue(ctx, value, recurseTimes) {
      // Provide a hook for user-specified inspect functions.
      // Check that value is an object with an inspect function on it
      if (ctx.customInspect &&
          value &&
          isFunction(value.inspect) &&
          // Filter out the util module, it's inspect function is special
          value.inspect !== exports.inspect &&
          // Also filter out any prototype objects using the circular check.
          !(value.constructor && value.constructor.prototype === value)) {
        var ret = value.inspect(recurseTimes, ctx);
        if (!isString(ret)) {
          ret = formatValue(ctx, ret, recurseTimes);
        }
        return ret;
      }
    
      // Primitive types cannot have properties
      var primitive = formatPrimitive(ctx, value);
      if (primitive) {
        return primitive;
      }
    
      // Look up the keys of the object.
      var keys = Object.keys(value);
      var visibleKeys = arrayToHash(keys);
    
      if (ctx.showHidden) {
        keys = Object.getOwnPropertyNames(value);
      }
    
      // IE doesn't make error fields non-enumerable
      // http://msdn.microsoft.com/en-us/library/ie/dww52sbt(v=vs.94).aspx
      if (isError(value)
          && (keys.indexOf('message') >= 0 || keys.indexOf('description') >= 0)) {
        return formatError(value);
      }
    
      // Some type of object without properties can be shortcutted.
      if (keys.length === 0) {
        if (isFunction(value)) {
          var name = value.name ? ': ' + value.name : '';
          return ctx.stylize('[Function' + name + ']', 'special');
        }
        if (isRegExp(value)) {
          return ctx.stylize(RegExp.prototype.toString.call(value), 'regexp');
        }
        if (isDate(value)) {
          return ctx.stylize(Date.prototype.toString.call(value), 'date');
        }
        if (isError(value)) {
          return formatError(value);
        }
      }
    
      var base = '', array = false, braces = ['{', '}'];
    
      // Make Array say that they are Array
      if (isArray(value)) {
        array = true;
        braces = ['[', ']'];
      }
    
      // Make functions say that they are functions
      if (isFunction(value)) {
        var n = value.name ? ': ' + value.name : '';
        base = ' [Function' + n + ']';
      }
    
      // Make RegExps say that they are RegExps
      if (isRegExp(value)) {
        base = ' ' + RegExp.prototype.toString.call(value);
      }
    
      // Make dates with properties first say the date
      if (isDate(value)) {
        base = ' ' + Date.prototype.toUTCString.call(value);
      }
    
      // Make error with message first say the error
      if (isError(value)) {
        base = ' ' + formatError(value);
      }
    
      if (keys.length === 0 && (!array || value.length == 0)) {
        return braces[0] + base + braces[1];
      }
    
      if (recurseTimes < 0) {
        if (isRegExp(value)) {
          return ctx.stylize(RegExp.prototype.toString.call(value), 'regexp');
        } else {
          return ctx.stylize('[Object]', 'special');
        }
      }
    
      ctx.seen.push(value);
    
      var output;
      if (array) {
        output = formatArray(ctx, value, recurseTimes, visibleKeys, keys);
      } else {
        output = keys.map(function(key) {
          return formatProperty(ctx, value, recurseTimes, visibleKeys, key, array);
        });
      }
    
      ctx.seen.pop();
    
      return reduceToSingleString(output, base, braces);
    }
    
    
    function formatPrimitive(ctx, value) {
      if (isUndefined(value))
        return ctx.stylize('undefined', 'undefined');
      if (isString(value)) {
        var simple = '\'' + JSON.stringify(value).replace(/^"|"$/g, '')
                                                 .replace(/'/g, "\\'")
                                                 .replace(/\\"/g, '"') + '\'';
        return ctx.stylize(simple, 'string');
      }
      if (isNumber(value))
        return ctx.stylize('' + value, 'number');
      if (isBoolean(value))
        return ctx.stylize('' + value, 'boolean');
      // For some reason typeof null is "object", so special case here.
      if (isNull(value))
        return ctx.stylize('null', 'null');
    }
    
    
    function formatError(value) {
      return '[' + Error.prototype.toString.call(value) + ']';
    }
    
    
    function formatArray(ctx, value, recurseTimes, visibleKeys, keys) {
      var output = [];
      for (var i = 0, l = value.length; i < l; ++i) {
        if (hasOwnProperty(value, String(i))) {
          output.push(formatProperty(ctx, value, recurseTimes, visibleKeys,
              String(i), true));
        } else {
          output.push('');
        }
      }
      keys.forEach(function(key) {
        if (!key.match(/^\d+$/)) {
          output.push(formatProperty(ctx, value, recurseTimes, visibleKeys,
              key, true));
        }
      });
      return output;
    }
    
    
    function formatProperty(ctx, value, recurseTimes, visibleKeys, key, array) {
      var name, str, desc;
      desc = Object.getOwnPropertyDescriptor(value, key) || { value: value[key] };
      if (desc.get) {
        if (desc.set) {
          str = ctx.stylize('[Getter/Setter]', 'special');
        } else {
          str = ctx.stylize('[Getter]', 'special');
        }
      } else {
        if (desc.set) {
          str = ctx.stylize('[Setter]', 'special');
        }
      }
      if (!hasOwnProperty(visibleKeys, key)) {
        name = '[' + key + ']';
      }
      if (!str) {
        if (ctx.seen.indexOf(desc.value) < 0) {
          if (isNull(recurseTimes)) {
            str = formatValue(ctx, desc.value, null);
          } else {
            str = formatValue(ctx, desc.value, recurseTimes - 1);
          }
          if (str.indexOf('\n') > -1) {
            if (array) {
              str = str.split('\n').map(function(line) {
                return '  ' + line;
              }).join('\n').substr(2);
            } else {
              str = '\n' + str.split('\n').map(function(line) {
                return '   ' + line;
              }).join('\n');
            }
          }
        } else {
          str = ctx.stylize('[Circular]', 'special');
        }
      }
      if (isUndefined(name)) {
        if (array && key.match(/^\d+$/)) {
          return str;
        }
        name = JSON.stringify('' + key);
        if (name.match(/^"([a-zA-Z_][a-zA-Z_0-9]*)"$/)) {
          name = name.substr(1, name.length - 2);
          name = ctx.stylize(name, 'name');
        } else {
          name = name.replace(/'/g, "\\'")
                     .replace(/\\"/g, '"')
                     .replace(/(^"|"$)/g, "'");
          name = ctx.stylize(name, 'string');
        }
      }
    
      return name + ': ' + str;
    }
    
    
    function reduceToSingleString(output, base, braces) {
      var numLinesEst = 0;
      var length = output.reduce(function(prev, cur) {
        numLinesEst++;
        if (cur.indexOf('\n') >= 0) numLinesEst++;
        return prev + cur.replace(/\u001b\[\d\d?m/g, '').length + 1;
      }, 0);
    
      if (length > 60) {
        return braces[0] +
               (base === '' ? '' : base + '\n ') +
               ' ' +
               output.join(',\n  ') +
               ' ' +
               braces[1];
      }
    
      return braces[0] + base + ' ' + output.join(', ') + ' ' + braces[1];
    }
    
    
    // NOTE: These type checking functions intentionally don't use `instanceof`
    // because it is fragile and can be easily faked with `Object.create()`.
    function isArray(ar) {
      return Array.isArray(ar);
    }
    exports.isArray = isArray;
    
    function isBoolean(arg) {
      return typeof arg === 'boolean';
    }
    exports.isBoolean = isBoolean;
    
    function isNull(arg) {
      return arg === null;
    }
    exports.isNull = isNull;
    
    function isNullOrUndefined(arg) {
      return arg == null;
    }
    exports.isNullOrUndefined = isNullOrUndefined;
    
    function isNumber(arg) {
      return typeof arg === 'number';
    }
    exports.isNumber = isNumber;
    
    function isString(arg) {
      return typeof arg === 'string';
    }
    exports.isString = isString;
    
    function isSymbol(arg) {
      return typeof arg === 'symbol';
    }
    exports.isSymbol = isSymbol;
    
    function isUndefined(arg) {
      return arg === void 0;
    }
    exports.isUndefined = isUndefined;
    
    function isRegExp(re) {
      return isObject(re) && objectToString(re) === '[object RegExp]';
    }
    exports.isRegExp = isRegExp;
    
    function isObject(arg) {
      return typeof arg === 'object' && arg !== null;
    }
    exports.isObject = isObject;
    
    function isDate(d) {
      return isObject(d) && objectToString(d) === '[object Date]';
    }
    exports.isDate = isDate;
    
    function isError(e) {
      return isObject(e) &&
          (objectToString(e) === '[object Error]' || e instanceof Error);
    }
    exports.isError = isError;
    
    function isFunction(arg) {
      return typeof arg === 'function';
    }
    exports.isFunction = isFunction;
    
    function isPrimitive(arg) {
      return arg === null ||
             typeof arg === 'boolean' ||
             typeof arg === 'number' ||
             typeof arg === 'string' ||
             typeof arg === 'symbol' ||  // ES6 symbol
             typeof arg === 'undefined';
    }
    exports.isPrimitive = isPrimitive;
    
    exports.isBuffer = require('./support/isBuffer');
    
    function objectToString(o) {
      return Object.prototype.toString.call(o);
    }
    
    
    function pad(n) {
      return n < 10 ? '0' + n.toString(10) : n.toString(10);
    }
    
    
    var months = ['Jan', 'Feb', 'Mar', 'Apr', 'May', 'Jun', 'Jul', 'Aug', 'Sep',
                  'Oct', 'Nov', 'Dec'];
    
    // 26 Feb 16:19:34
    function timestamp() {
      var d = new Date();
      var time = [pad(d.getHours()),
                  pad(d.getMinutes()),
                  pad(d.getSeconds())].join(':');
      return [d.getDate(), months[d.getMonth()], time].join(' ');
    }
    
    
    // log is just a thin wrapper to console.log that prepends a timestamp
    exports.log = function() {
      console.log('%s - %s', timestamp(), exports.format.apply(exports, arguments));
    };
    
    
    /**
     * Inherit the prototype methods from one constructor into another.
     *
     * The Function.prototype.inherits from lang.js rewritten as a standalone
     * function (not on Function.prototype). NOTE: If this file is to be loaded
     * during bootstrapping this function needs to be rewritten using some native
     * functions as prototype setup using normal JavaScript does not work as
     * expected during bootstrapping (see mirror.js in r114903).
     *
     * @param {function} ctor Constructor function which needs to inherit the
     *     prototype.
     * @param {function} superCtor Constructor function to inherit prototype from.
     */
    exports.inherits = require('inherits');
    
    exports._extend = function(origin, add) {
      // Don't do anything if add isn't an object
      if (!add || !isObject(add)) return origin;
    
      var keys = Object.keys(add);
      var i = keys.length;
      while (i--) {
        origin[keys[i]] = add[keys[i]];
      }
      return origin;
    };
    
    function hasOwnProperty(obj, prop) {
      return Object.prototype.hasOwnProperty.call(obj, prop);
    }
    
    }).call(this,require('_process'),typeof global !== "undefined" ? global : typeof self !== "undefined" ? self : typeof window !== "undefined" ? window : {})
    },{"./support/isBuffer":36,"_process":22,"inherits":20}],38:[function(require,module,exports){
    (function (Buffer){
    (function () {
      "use strict";
    
      function btoa(str) {
        var buffer
          ;
    
        if (str instanceof Buffer) {
          buffer = str;
        } else {
          buffer = new Buffer(str.toString(), 'binary');
        }
    
        return buffer.toString('base64');
      }
    
      module.exports = btoa;
    }());
    
    }).call(this,require("buffer").Buffer)
    },{"buffer":3}],39:[function(require,module,exports){
    /*!
     * jQuery JavaScript Library v2.1.1
     * http://jquery.com/
     *
     * Includes Sizzle.js
     * http://sizzlejs.com/
     *
     * Copyright 2005, 2014 jQuery Foundation, Inc. and other contributors
     * Released under the MIT license
     * http://jquery.org/license
     *
     * Date: 2014-05-01T17:11Z
     */
    
    (function( global, factory ) {
    
        if ( typeof module === "object" && typeof module.exports === "object" ) {
            // For CommonJS and CommonJS-like environments where a proper window is present,
            // execute the factory and get jQuery
            // For environments that do not inherently posses a window with a document
            // (such as Node.js), expose a jQuery-making factory as module.exports
            // This accentuates the need for the creation of a real window
            // e.g. var jQuery = require("jquery")(window);
            // See ticket #14549 for more info
            module.exports = global.document ?
                factory( global, true ) :
                function( w ) {
                    if ( !w.document ) {
                        throw new Error( "jQuery requires a window with a document" );
                    }
                    return factory( w );
                };
        } else {
            factory( global );
        }
    
    // Pass this if window is not defined yet
    }(typeof window !== "undefined" ? window : this, function( window, noGlobal ) {
    
    // Can't do this because several apps including ASP.NET trace
    // the stack via arguments.caller.callee and Firefox dies if
    // you try to trace through "use strict" call chains. (#13335)
    // Support: Firefox 18+
    //
    
    var arr = [];
    
    var slice = arr.slice;
    
    var concat = arr.concat;
    
    var push = arr.push;
    
    var indexOf = arr.indexOf;
    
    var class2type = {};
    
    var toString = class2type.toString;
    
    var hasOwn = class2type.hasOwnProperty;
    
    var support = {};
    
    
    
    var
        // Use the correct document accordingly with window argument (sandbox)
        document = window.document,
    
        version = "2.1.1",
    
        // Define a local copy of jQuery
        jQuery = function( selector, context ) {
            // The jQuery object is actually just the init constructor 'enhanced'
            // Need init if jQuery is called (just allow error to be thrown if not included)
            return new jQuery.fn.init( selector, context );
        },
    
        // Support: Android<4.1
        // Make sure we trim BOM and NBSP
        rtrim = /^[\s\uFEFF\xA0]+|[\s\uFEFF\xA0]+$/g,
    
        // Matches dashed string for camelizing
        rmsPrefix = /^-ms-/,
        rdashAlpha = /-([\da-z])/gi,
    
        // Used by jQuery.camelCase as callback to replace()
        fcamelCase = function( all, letter ) {
            return letter.toUpperCase();
        };
    
    jQuery.fn = jQuery.prototype = {
        // The current version of jQuery being used
        jquery: version,
    
        constructor: jQuery,
    
        // Start with an empty selector
        selector: "",
    
        // The default length of a jQuery object is 0
        length: 0,
    
        toArray: function() {
            return slice.call( this );
        },
    
        // Get the Nth element in the matched element set OR
        // Get the whole matched element set as a clean array
        get: function( num ) {
            return num != null ?
    
                // Return just the one element from the set
                ( num < 0 ? this[ num + this.length ] : this[ num ] ) :
    
                // Return all the elements in a clean array
                slice.call( this );
        },
    
        // Take an array of elements and push it onto the stack
        // (returning the new matched element set)
        pushStack: function( elems ) {
    
            // Build a new jQuery matched element set
            var ret = jQuery.merge( this.constructor(), elems );
    
            // Add the old object onto the stack (as a reference)
            ret.prevObject = this;
            ret.context = this.context;
    
            // Return the newly-formed element set
            return ret;
        },
    
        // Execute a callback for every element in the matched set.
        // (You can seed the arguments with an array of args, but this is
        // only used internally.)
        each: function( callback, args ) {
            return jQuery.each( this, callback, args );
        },
    
        map: function( callback ) {
            return this.pushStack( jQuery.map(this, function( elem, i ) {
                return callback.call( elem, i, elem );
            }));
        },
    
        slice: function() {
            return this.pushStack( slice.apply( this, arguments ) );
        },
    
        first: function() {
            return this.eq( 0 );
        },
    
        last: function() {
            return this.eq( -1 );
        },
    
        eq: function( i ) {
            var len = this.length,
                j = +i + ( i < 0 ? len : 0 );
            return this.pushStack( j >= 0 && j < len ? [ this[j] ] : [] );
        },
    
        end: function() {
            return this.prevObject || this.constructor(null);
        },
    
        // For internal use only.
        // Behaves like an Array's method, not like a jQuery method.
        push: push,
        sort: arr.sort,
        splice: arr.splice
    };
    
    jQuery.extend = jQuery.fn.extend = function() {
        var options, name, src, copy, copyIsArray, clone,
            target = arguments[0] || {},
            i = 1,
            length = arguments.length,
            deep = false;
    
        // Handle a deep copy situation
        if ( typeof target === "boolean" ) {
            deep = target;
    
            // skip the boolean and the target
            target = arguments[ i ] || {};
            i++;
        }
    
        // Handle case when target is a string or something (possible in deep copy)
        if ( typeof target !== "object" && !jQuery.isFunction(target) ) {
            target = {};
        }
    
        // extend jQuery itself if only one argument is passed
        if ( i === length ) {
            target = this;
            i--;
        }
    
        for ( ; i < length; i++ ) {
            // Only deal with non-null/undefined values
            if ( (options = arguments[ i ]) != null ) {
                // Extend the base object
                for ( name in options ) {
                    src = target[ name ];
                    copy = options[ name ];
    
                    // Prevent never-ending loop
                    if ( target === copy ) {
                        continue;
                    }
    
                    // Recurse if we're merging plain objects or arrays
                    if ( deep && copy && ( jQuery.isPlainObject(copy) || (copyIsArray = jQuery.isArray(copy)) ) ) {
                        if ( copyIsArray ) {
                            copyIsArray = false;
                            clone = src && jQuery.isArray(src) ? src : [];
    
                        } else {
                            clone = src && jQuery.isPlainObject(src) ? src : {};
                        }
    
                        // Never move original objects, clone them
                        target[ name ] = jQuery.extend( deep, clone, copy );
    
                    // Don't bring in undefined values
                    } else if ( copy !== undefined ) {
                        target[ name ] = copy;
                    }
                }
            }
        }
    
        // Return the modified object
        return target;
    };
    
    jQuery.extend({
        // Unique for each copy of jQuery on the page
        expando: "jQuery" + ( version + Math.random() ).replace( /\D/g, "" ),
    
        // Assume jQuery is ready without the ready module
        isReady: true,
    
        error: function( msg ) {
            throw new Error( msg );
        },
    
        noop: function() {},
    
        // See test/unit/core.js for details concerning isFunction.
        // Since version 1.3, DOM methods and functions like alert
        // aren't supported. They return false on IE (#2968).
        isFunction: function( obj ) {
            return jQuery.type(obj) === "function";
        },
    
        isArray: Array.isArray,
    
        isWindow: function( obj ) {
            return obj != null && obj === obj.window;
        },
    
        isNumeric: function( obj ) {
            // parseFloat NaNs numeric-cast false positives (null|true|false|"")
            // ...but misinterprets leading-number strings, particularly hex literals ("0x...")
            // subtraction forces infinities to NaN
            return !jQuery.isArray( obj ) && obj - parseFloat( obj ) >= 0;
        },
    
        isPlainObject: function( obj ) {
            // Not plain objects:
            // - Any object or value whose internal [[Class]] property is not "[object Object]"
            // - DOM nodes
            // - window
            if ( jQuery.type( obj ) !== "object" || obj.nodeType || jQuery.isWindow( obj ) ) {
                return false;
            }
    
            if ( obj.constructor &&
                    !hasOwn.call( obj.constructor.prototype, "isPrototypeOf" ) ) {
                return false;
            }
    
            // If the function hasn't returned already, we're confident that
            // |obj| is a plain object, created by {} or constructed with new Object
            return true;
        },
    
        isEmptyObject: function( obj ) {
            var name;
            for ( name in obj ) {
                return false;
            }
            return true;
        },
    
        type: function( obj ) {
            if ( obj == null ) {
                return obj + "";
            }
            // Support: Android < 4.0, iOS < 6 (functionish RegExp)
            return typeof obj === "object" || typeof obj === "function" ?
                class2type[ toString.call(obj) ] || "object" :
                typeof obj;
        },
    
        // Evaluates a script in a global context
        globalEval: function( code ) {
            var script,
                indirect = eval;
    
            code = jQuery.trim( code );
    
            if ( code ) {
                // If the code includes a valid, prologue position
                // strict mode pragma, execute code by injecting a
                // script tag into the document.
                if ( code.indexOf("use strict") === 1 ) {
                    script = document.createElement("script");
                    script.text = code;
                    document.head.appendChild( script ).parentNode.removeChild( script );
                } else {
                // Otherwise, avoid the DOM node creation, insertion
                // and removal by using an indirect global eval
                    indirect( code );
                }
            }
        },
    
        // Convert dashed to camelCase; used by the css and data modules
        // Microsoft forgot to hump their vendor prefix (#9572)
        camelCase: function( string ) {
            return string.replace( rmsPrefix, "ms-" ).replace( rdashAlpha, fcamelCase );
        },
    
        nodeName: function( elem, name ) {
            return elem.nodeName && elem.nodeName.toLowerCase() === name.toLowerCase();
        },
    
        // args is for internal usage only
        each: function( obj, callback, args ) {
            var value,
                i = 0,
                length = obj.length,
                isArray = isArraylike( obj );
    
            if ( args ) {
                if ( isArray ) {
                    for ( ; i < length; i++ ) {
                        value = callback.apply( obj[ i ], args );
    
                        if ( value === false ) {
                            break;
                        }
                    }
                } else {
                    for ( i in obj ) {
                        value = callback.apply( obj[ i ], args );
    
                        if ( value === false ) {
                            break;
                        }
                    }
                }
    
            // A special, fast, case for the most common use of each
            } else {
                if ( isArray ) {
                    for ( ; i < length; i++ ) {
                        value = callback.call( obj[ i ], i, obj[ i ] );
    
                        if ( value === false ) {
                            break;
                        }
                    }
                } else {
                    for ( i in obj ) {
                        value = callback.call( obj[ i ], i, obj[ i ] );
    
                        if ( value === false ) {
                            break;
                        }
                    }
                }
            }
    
            return obj;
        },
    
        // Support: Android<4.1
        trim: function( text ) {
            return text == null ?
                "" :
                ( text + "" ).replace( rtrim, "" );
        },
    
        // results is for internal usage only
        makeArray: function( arr, results ) {
            var ret = results || [];
    
            if ( arr != null ) {
                if ( isArraylike( Object(arr) ) ) {
                    jQuery.merge( ret,
                        typeof arr === "string" ?
                        [ arr ] : arr
                    );
                } else {
                    push.call( ret, arr );
                }
            }
    
            return ret;
        },
    
        inArray: function( elem, arr, i ) {
            return arr == null ? -1 : indexOf.call( arr, elem, i );
        },
    
        merge: function( first, second ) {
            var len = +second.length,
                j = 0,
                i = first.length;
    
            for ( ; j < len; j++ ) {
                first[ i++ ] = second[ j ];
            }
    
            first.length = i;
    
            return first;
        },
    
        grep: function( elems, callback, invert ) {
            var callbackInverse,
                matches = [],
                i = 0,
                length = elems.length,
                callbackExpect = !invert;
    
            // Go through the array, only saving the items
            // that pass the validator function
            for ( ; i < length; i++ ) {
                callbackInverse = !callback( elems[ i ], i );
                if ( callbackInverse !== callbackExpect ) {
                    matches.push( elems[ i ] );
                }
            }
    
            return matches;
        },
    
        // arg is for internal usage only
        map: function( elems, callback, arg ) {
            var value,
                i = 0,
                length = elems.length,
                isArray = isArraylike( elems ),
                ret = [];
    
            // Go through the array, translating each of the items to their new values
            if ( isArray ) {
                for ( ; i < length; i++ ) {
                    value = callback( elems[ i ], i, arg );
    
                    if ( value != null ) {
                        ret.push( value );
                    }
                }
    
            // Go through every key on the object,
            } else {
                for ( i in elems ) {
                    value = callback( elems[ i ], i, arg );
    
                    if ( value != null ) {
                        ret.push( value );
                    }
                }
            }
    
            // Flatten any nested arrays
            return concat.apply( [], ret );
        },
    
        // A global GUID counter for objects
        guid: 1,
    
        // Bind a function to a context, optionally partially applying any
        // arguments.
        proxy: function( fn, context ) {
            var tmp, args, proxy;
    
            if ( typeof context === "string" ) {
                tmp = fn[ context ];
                context = fn;
                fn = tmp;
            }
    
            // Quick check to determine if target is callable, in the spec
            // this throws a TypeError, but we will just return undefined.
            if ( !jQuery.isFunction( fn ) ) {
                return undefined;
            }
    
            // Simulated bind
            args = slice.call( arguments, 2 );
            proxy = function() {
                return fn.apply( context || this, args.concat( slice.call( arguments ) ) );
            };
    
            // Set the guid of unique handler to the same of original handler, so it can be removed
            proxy.guid = fn.guid = fn.guid || jQuery.guid++;
    
            return proxy;
        },
    
        now: Date.now,
    
        // jQuery.support is not used in Core but other projects attach their
        // properties to it so it needs to exist.
        support: support
    });
    
    // Populate the class2type map
    jQuery.each("Boolean Number String Function Array Date RegExp Object Error".split(" "), function(i, name) {
        class2type[ "[object " + name + "]" ] = name.toLowerCase();
    });
    
    function isArraylike( obj ) {
        var length = obj.length,
            type = jQuery.type( obj );
    
        if ( type === "function" || jQuery.isWindow( obj ) ) {
            return false;
        }
    
        if ( obj.nodeType === 1 && length ) {
            return true;
        }
    
        return type === "array" || length === 0 ||
            typeof length === "number" && length > 0 && ( length - 1 ) in obj;
    }
    var Sizzle =
    /*!
     * Sizzle CSS Selector Engine v1.10.19
     * http://sizzlejs.com/
     *
     * Copyright 2013 jQuery Foundation, Inc. and other contributors
     * Released under the MIT license
     * http://jquery.org/license
     *
     * Date: 2014-04-18
     */
    (function( window ) {
    
    var i,
        support,
        Expr,
        getText,
        isXML,
        tokenize,
        compile,
        select,
        outermostContext,
        sortInput,
        hasDuplicate,
    
        // Local document vars
        setDocument,
        document,
        docElem,
        documentIsHTML,
        rbuggyQSA,
        rbuggyMatches,
        matches,
        contains,
    
        // Instance-specific data
        expando = "sizzle" + -(new Date()),
        preferredDoc = window.document,
        dirruns = 0,
        done = 0,
        classCache = createCache(),
        tokenCache = createCache(),
        compilerCache = createCache(),
        sortOrder = function( a, b ) {
            if ( a === b ) {
                hasDuplicate = true;
            }
            return 0;
        },
    
        // General-purpose constants
        strundefined = typeof undefined,
        MAX_NEGATIVE = 1 << 31,
    
        // Instance methods
        hasOwn = ({}).hasOwnProperty,
        arr = [],
        pop = arr.pop,
        push_native = arr.push,
        push = arr.push,
        slice = arr.slice,
        // Use a stripped-down indexOf if we can't use a native one
        indexOf = arr.indexOf || function( elem ) {
            var i = 0,
                len = this.length;
            for ( ; i < len; i++ ) {
                if ( this[i] === elem ) {
                    return i;
                }
            }
            return -1;
        },
    
        booleans = "checked|selected|async|autofocus|autoplay|controls|defer|disabled|hidden|ismap|loop|multiple|open|readonly|required|scoped",
    
        // Regular expressions
    
        // Whitespace characters http://www.w3.org/TR/css3-selectors/#whitespace
        whitespace = "[\\x20\\t\\r\\n\\f]",
        // http://www.w3.org/TR/css3-syntax/#characters
        characterEncoding = "(?:\\\\.|[\\w-]|[^\\x00-\\xa0])+",
    
        // Loosely modeled on CSS identifier characters
        // An unquoted value should be a CSS identifier http://www.w3.org/TR/css3-selectors/#attribute-selectors
        // Proper syntax: http://www.w3.org/TR/CSS21/syndata.html#value-def-identifier
        identifier = characterEncoding.replace( "w", "w#" ),
    
        // Attribute selectors: http://www.w3.org/TR/selectors/#attribute-selectors
        attributes = "\\[" + whitespace + "*(" + characterEncoding + ")(?:" + whitespace +
            // Operator (capture 2)
            "*([*^$|!~]?=)" + whitespace +
            // "Attribute values must be CSS identifiers [capture 5] or strings [capture 3 or capture 4]"
            "*(?:'((?:\\\\.|[^\\\\'])*)'|\"((?:\\\\.|[^\\\\\"])*)\"|(" + identifier + "))|)" + whitespace +
            "*\\]",
    
        pseudos = ":(" + characterEncoding + ")(?:\\((" +
            // To reduce the number of selectors needing tokenize in the preFilter, prefer arguments:
            // 1. quoted (capture 3; capture 4 or capture 5)
            "('((?:\\\\.|[^\\\\'])*)'|\"((?:\\\\.|[^\\\\\"])*)\")|" +
            // 2. simple (capture 6)
            "((?:\\\\.|[^\\\\()[\\]]|" + attributes + ")*)|" +
            // 3. anything else (capture 2)
            ".*" +
            ")\\)|)",
    
        // Leading and non-escaped trailing whitespace, capturing some non-whitespace characters preceding the latter
        rtrim = new RegExp( "^" + whitespace + "+|((?:^|[^\\\\])(?:\\\\.)*)" + whitespace + "+$", "g" ),
    
        rcomma = new RegExp( "^" + whitespace + "*," + whitespace + "*" ),
        rcombinators = new RegExp( "^" + whitespace + "*([>+~]|" + whitespace + ")" + whitespace + "*" ),
    
        rattributeQuotes = new RegExp( "=" + whitespace + "*([^\\]'\"]*?)" + whitespace + "*\\]", "g" ),
    
        rpseudo = new RegExp( pseudos ),
        ridentifier = new RegExp( "^" + identifier + "$" ),
    
        matchExpr = {
            "ID": new RegExp( "^#(" + characterEncoding + ")" ),
            "CLASS": new RegExp( "^\\.(" + characterEncoding + ")" ),
            "TAG": new RegExp( "^(" + characterEncoding.replace( "w", "w*" ) + ")" ),
            "ATTR": new RegExp( "^" + attributes ),
            "PSEUDO": new RegExp( "^" + pseudos ),
            "CHILD": new RegExp( "^:(only|first|last|nth|nth-last)-(child|of-type)(?:\\(" + whitespace +
                "*(even|odd|(([+-]|)(\\d*)n|)" + whitespace + "*(?:([+-]|)" + whitespace +
                "*(\\d+)|))" + whitespace + "*\\)|)", "i" ),
            "bool": new RegExp( "^(?:" + booleans + ")$", "i" ),
            // For use in libraries implementing .is()
            // We use this for POS matching in `select`
            "needsContext": new RegExp( "^" + whitespace + "*[>+~]|:(even|odd|eq|gt|lt|nth|first|last)(?:\\(" +
                whitespace + "*((?:-\\d)?\\d*)" + whitespace + "*\\)|)(?=[^-]|$)", "i" )
        },
    
        rinputs = /^(?:input|select|textarea|button)$/i,
        rheader = /^h\d$/i,
    
        rnative = /^[^{]+\{\s*\[native \w/,
    
        // Easily-parseable/retrievable ID or TAG or CLASS selectors
        rquickExpr = /^(?:#([\w-]+)|(\w+)|\.([\w-]+))$/,
    
        rsibling = /[+~]/,
        rescape = /'|\\/g,
    
        // CSS escapes http://www.w3.org/TR/CSS21/syndata.html#escaped-characters
        runescape = new RegExp( "\\\\([\\da-f]{1,6}" + whitespace + "?|(" + whitespace + ")|.)", "ig" ),
        funescape = function( _, escaped, escapedWhitespace ) {
            var high = "0x" + escaped - 0x10000;
            // NaN means non-codepoint
            // Support: Firefox<24
            // Workaround erroneous numeric interpretation of +"0x"
            return high !== high || escapedWhitespace ?
                escaped :
                high < 0 ?
                    // BMP codepoint
                    String.fromCharCode( high + 0x10000 ) :
                    // Supplemental Plane codepoint (surrogate pair)
                    String.fromCharCode( high >> 10 | 0xD800, high & 0x3FF | 0xDC00 );
        };
    
    // Optimize for push.apply( _, NodeList )
    try {
        push.apply(
            (arr = slice.call( preferredDoc.childNodes )),
            preferredDoc.childNodes
        );
        // Support: Android<4.0
        // Detect silently failing push.apply
        arr[ preferredDoc.childNodes.length ].nodeType;
    } catch ( e ) {
        push = { apply: arr.length ?
    
            // Leverage slice if possible
            function( target, els ) {
                push_native.apply( target, slice.call(els) );
            } :
    
            // Support: IE<9
            // Otherwise append directly
            function( target, els ) {
                var j = target.length,
                    i = 0;
                // Can't trust NodeList.length
                while ( (target[j++] = els[i++]) ) {}
                target.length = j - 1;
            }
        };
    }
    
    function Sizzle( selector, context, results, seed ) {
        var match, elem, m, nodeType,
            // QSA vars
            i, groups, old, nid, newContext, newSelector;
    
        if ( ( context ? context.ownerDocument || context : preferredDoc ) !== document ) {
            setDocument( context );
        }
    
        context = context || document;
        results = results || [];
    
        if ( !selector || typeof selector !== "string" ) {
            return results;
        }
    
        if ( (nodeType = context.nodeType) !== 1 && nodeType !== 9 ) {
            return [];
        }
    
        if ( documentIsHTML && !seed ) {
    
            // Shortcuts
            if ( (match = rquickExpr.exec( selector )) ) {
                // Speed-up: Sizzle("#ID")
                if ( (m = match[1]) ) {
                    if ( nodeType === 9 ) {
                        elem = context.getElementById( m );
                        // Check parentNode to catch when Blackberry 4.6 returns
                        // nodes that are no longer in the document (jQuery #6963)
                        if ( elem && elem.parentNode ) {
                            // Handle the case where IE, Opera, and Webkit return items
                            // by name instead of ID
                            if ( elem.id === m ) {
                                results.push( elem );
                                return results;
                            }
                        } else {
                            return results;
                        }
                    } else {
                        // Context is not a document
                        if ( context.ownerDocument && (elem = context.ownerDocument.getElementById( m )) &&
                            contains( context, elem ) && elem.id === m ) {
                            results.push( elem );
                            return results;
                        }
                    }
    
                // Speed-up: Sizzle("TAG")
                } else if ( match[2] ) {
                    push.apply( results, context.getElementsByTagName( selector ) );
                    return results;
    
                // Speed-up: Sizzle(".CLASS")
                } else if ( (m = match[3]) && support.getElementsByClassName && context.getElementsByClassName ) {
                    push.apply( results, context.getElementsByClassName( m ) );
                    return results;
                }
            }
    
            // QSA path
            if ( support.qsa && (!rbuggyQSA || !rbuggyQSA.test( selector )) ) {
                nid = old = expando;
                newContext = context;
                newSelector = nodeType === 9 && selector;
    
                // qSA works strangely on Element-rooted queries
                // We can work around this by specifying an extra ID on the root
                // and working up from there (Thanks to Andrew Dupont for the technique)
                // IE 8 doesn't work on object elements
                if ( nodeType === 1 && context.nodeName.toLowerCase() !== "object" ) {
                    groups = tokenize( selector );
    
                    if ( (old = context.getAttribute("id")) ) {
                        nid = old.replace( rescape, "\\$&" );
                    } else {
                        context.setAttribute( "id", nid );
                    }
                    nid = "[id='" + nid + "'] ";
    
                    i = groups.length;
                    while ( i-- ) {
                        groups[i] = nid + toSelector( groups[i] );
                    }
                    newContext = rsibling.test( selector ) && testContext( context.parentNode ) || context;
                    newSelector = groups.join(",");
                }
    
                if ( newSelector ) {
                    try {
                        push.apply( results,
                            newContext.querySelectorAll( newSelector )
                        );
                        return results;
                    } catch(qsaError) {
                    } finally {
                        if ( !old ) {
                            context.removeAttribute("id");
                        }
                    }
                }
            }
        }
    
        // All others
        return select( selector.replace( rtrim, "$1" ), context, results, seed );
    }
    
    /**
     * Create key-value caches of limited size
     * @returns {Function(string, Object)} Returns the Object data after storing it on itself with
     *	property name the (space-suffixed) string and (if the cache is larger than Expr.cacheLength)
     *	deleting the oldest entry
     */
    function createCache() {
        var keys = [];
    
        function cache( key, value ) {
            // Use (key + " ") to avoid collision with native prototype properties (see Issue #157)
            if ( keys.push( key + " " ) > Expr.cacheLength ) {
                // Only keep the most recent entries
                delete cache[ keys.shift() ];
            }
            return (cache[ key + " " ] = value);
        }
        return cache;
    }
    
    /**
     * Mark a function for special use by Sizzle
     * @param {Function} fn The function to mark
     */
    function markFunction( fn ) {
        fn[ expando ] = true;
        return fn;
    }
    
    /**
     * Support testing using an element
     * @param {Function} fn Passed the created div and expects a boolean result
     */
    function assert( fn ) {
        var div = document.createElement("div");
    
        try {
            return !!fn( div );
        } catch (e) {
            return false;
        } finally {
            // Remove from its parent by default
            if ( div.parentNode ) {
                div.parentNode.removeChild( div );
            }
            // release memory in IE
            div = null;
        }
    }
    
    /**
     * Adds the same handler for all of the specified attrs
     * @param {String} attrs Pipe-separated list of attributes
     * @param {Function} handler The method that will be applied
     */
    function addHandle( attrs, handler ) {
        var arr = attrs.split("|"),
            i = attrs.length;
    
        while ( i-- ) {
            Expr.attrHandle[ arr[i] ] = handler;
        }
    }
    
    /**
     * Checks document order of two siblings
     * @param {Element} a
     * @param {Element} b
     * @returns {Number} Returns less than 0 if a precedes b, greater than 0 if a follows b
     */
    function siblingCheck( a, b ) {
        var cur = b && a,
            diff = cur && a.nodeType === 1 && b.nodeType === 1 &&
                ( ~b.sourceIndex || MAX_NEGATIVE ) -
                ( ~a.sourceIndex || MAX_NEGATIVE );
    
        // Use IE sourceIndex if available on both nodes
        if ( diff ) {
            return diff;
        }
    
        // Check if b follows a
        if ( cur ) {
            while ( (cur = cur.nextSibling) ) {
                if ( cur === b ) {
                    return -1;
                }
            }
        }
    
        return a ? 1 : -1;
    }
    
    /**
     * Returns a function to use in pseudos for input types
     * @param {String} type
     */
    function createInputPseudo( type ) {
        return function( elem ) {
            var name = elem.nodeName.toLowerCase();
            return name === "input" && elem.type === type;
        };
    }
    
    /**
     * Returns a function to use in pseudos for buttons
     * @param {String} type
     */
    function createButtonPseudo( type ) {
        return function( elem ) {
            var name = elem.nodeName.toLowerCase();
            return (name === "input" || name === "button") && elem.type === type;
        };
    }
    
    /**
     * Returns a function to use in pseudos for positionals
     * @param {Function} fn
     */
    function createPositionalPseudo( fn ) {
        return markFunction(function( argument ) {
            argument = +argument;
            return markFunction(function( seed, matches ) {
                var j,
                    matchIndexes = fn( [], seed.length, argument ),
                    i = matchIndexes.length;
    
                // Match elements found at the specified indexes
                while ( i-- ) {
                    if ( seed[ (j = matchIndexes[i]) ] ) {
                        seed[j] = !(matches[j] = seed[j]);
                    }
                }
            });
        });
    }
    
    /**
     * Checks a node for validity as a Sizzle context
     * @param {Element|Object=} context
     * @returns {Element|Object|Boolean} The input node if acceptable, otherwise a falsy value
     */
    function testContext( context ) {
        return context && typeof context.getElementsByTagName !== strundefined && context;
    }
    
    // Expose support vars for convenience
    support = Sizzle.support = {};
    
    /**
     * Detects XML nodes
     * @param {Element|Object} elem An element or a document
     * @returns {Boolean} True iff elem is a non-HTML XML node
     */
    isXML = Sizzle.isXML = function( elem ) {
        // documentElement is verified for cases where it doesn't yet exist
        // (such as loading iframes in IE - #4833)
        var documentElement = elem && (elem.ownerDocument || elem).documentElement;
        return documentElement ? documentElement.nodeName !== "HTML" : false;
    };
    
    /**
     * Sets document-related variables once based on the current document
     * @param {Element|Object} [doc] An element or document object to use to set the document
     * @returns {Object} Returns the current document
     */
    setDocument = Sizzle.setDocument = function( node ) {
        var hasCompare,
            doc = node ? node.ownerDocument || node : preferredDoc,
            parent = doc.defaultView;
    
        // If no document and documentElement is available, return
        if ( doc === document || doc.nodeType !== 9 || !doc.documentElement ) {
            return document;
        }
    
        // Set our document
        document = doc;
        docElem = doc.documentElement;
    
        // Support tests
        documentIsHTML = !isXML( doc );
    
        // Support: IE>8
        // If iframe document is assigned to "document" variable and if iframe has been reloaded,
        // IE will throw "permission denied" error when accessing "document" variable, see jQuery #13936
        // IE6-8 do not support the defaultView property so parent will be undefined
        if ( parent && parent !== parent.top ) {
            // IE11 does not have attachEvent, so all must suffer
            if ( parent.addEventListener ) {
                parent.addEventListener( "unload", function() {
                    setDocument();
                }, false );
            } else if ( parent.attachEvent ) {
                parent.attachEvent( "onunload", function() {
                    setDocument();
                });
            }
        }
    
        /* Attributes
        ---------------------------------------------------------------------- */
    
        // Support: IE<8
        // Verify that getAttribute really returns attributes and not properties (excepting IE8 booleans)
        support.attributes = assert(function( div ) {
            div.className = "i";
            return !div.getAttribute("className");
        });
    
        /* getElement(s)By*
        ---------------------------------------------------------------------- */
    
        // Check if getElementsByTagName("*") returns only elements
        support.getElementsByTagName = assert(function( div ) {
            div.appendChild( doc.createComment("") );
            return !div.getElementsByTagName("*").length;
        });
    
        // Check if getElementsByClassName can be trusted
        support.getElementsByClassName = rnative.test( doc.getElementsByClassName ) && assert(function( div ) {
            div.innerHTML = "<div class='a'></div><div class='a i'></div>";
    
            // Support: Safari<4
            // Catch class over-caching
            div.firstChild.className = "i";
            // Support: Opera<10
            // Catch gEBCN failure to find non-leading classes
            return div.getElementsByClassName("i").length === 2;
        });
    
        // Support: IE<10
        // Check if getElementById returns elements by name
        // The broken getElementById methods don't pick up programatically-set names,
        // so use a roundabout getElementsByName test
        support.getById = assert(function( div ) {
            docElem.appendChild( div ).id = expando;
            return !doc.getElementsByName || !doc.getElementsByName( expando ).length;
        });
    
        // ID find and filter
        if ( support.getById ) {
            Expr.find["ID"] = function( id, context ) {
                if ( typeof context.getElementById !== strundefined && documentIsHTML ) {
                    var m = context.getElementById( id );
                    // Check parentNode to catch when Blackberry 4.6 returns
                    // nodes that are no longer in the document #6963
                    return m && m.parentNode ? [ m ] : [];
                }
            };
            Expr.filter["ID"] = function( id ) {
                var attrId = id.replace( runescape, funescape );
                return function( elem ) {
                    return elem.getAttribute("id") === attrId;
                };
            };
        } else {
            // Support: IE6/7
            // getElementById is not reliable as a find shortcut
            delete Expr.find["ID"];
    
            Expr.filter["ID"] =  function( id ) {
                var attrId = id.replace( runescape, funescape );
                return function( elem ) {
                    var node = typeof elem.getAttributeNode !== strundefined && elem.getAttributeNode("id");
                    return node && node.value === attrId;
                };
            };
        }
    
        // Tag
        Expr.find["TAG"] = support.getElementsByTagName ?
            function( tag, context ) {
                if ( typeof context.getElementsByTagName !== strundefined ) {
                    return context.getElementsByTagName( tag );
                }
            } :
            function( tag, context ) {
                var elem,
                    tmp = [],
                    i = 0,
                    results = context.getElementsByTagName( tag );
    
                // Filter out possible comments
                if ( tag === "*" ) {
                    while ( (elem = results[i++]) ) {
                        if ( elem.nodeType === 1 ) {
                            tmp.push( elem );
                        }
                    }
    
                    return tmp;
                }
                return results;
            };
    
        // Class
        Expr.find["CLASS"] = support.getElementsByClassName && function( className, context ) {
            if ( typeof context.getElementsByClassName !== strundefined && documentIsHTML ) {
                return context.getElementsByClassName( className );
            }
        };
    
        /* QSA/matchesSelector
        ---------------------------------------------------------------------- */
    
        // QSA and matchesSelector support
    
        // matchesSelector(:active) reports false when true (IE9/Opera 11.5)
        rbuggyMatches = [];
    
        // qSa(:focus) reports false when true (Chrome 21)
        // We allow this because of a bug in IE8/9 that throws an error
        // whenever `document.activeElement` is accessed on an iframe
        // So, we allow :focus to pass through QSA all the time to avoid the IE error
        // See http://bugs.jquery.com/ticket/13378
        rbuggyQSA = [];
    
        if ( (support.qsa = rnative.test( doc.querySelectorAll )) ) {
            // Build QSA regex
            // Regex strategy adopted from Diego Perini
            assert(function( div ) {
                // Select is set to empty string on purpose
                // This is to test IE's treatment of not explicitly
                // setting a boolean content attribute,
                // since its presence should be enough
                // http://bugs.jquery.com/ticket/12359
                div.innerHTML = "<select msallowclip=''><option selected=''></option></select>";
    
                // Support: IE8, Opera 11-12.16
                // Nothing should be selected when empty strings follow ^= or $= or *=
                // The test attribute must be unknown in Opera but "safe" for WinRT
                // http://msdn.microsoft.com/en-us/library/ie/hh465388.aspx#attribute_section
                if ( div.querySelectorAll("[msallowclip^='']").length ) {
                    rbuggyQSA.push( "[*^$]=" + whitespace + "*(?:''|\"\")" );
                }
    
                // Support: IE8
                // Boolean attributes and "value" are not treated correctly
                if ( !div.querySelectorAll("[selected]").length ) {
                    rbuggyQSA.push( "\\[" + whitespace + "*(?:value|" + booleans + ")" );
                }
    
                // Webkit/Opera - :checked should return selected option elements
                // http://www.w3.org/TR/2011/REC-css3-selectors-20110929/#checked
                // IE8 throws error here and will not see later tests
                if ( !div.querySelectorAll(":checked").length ) {
                    rbuggyQSA.push(":checked");
                }
            });
    
            assert(function( div ) {
                // Support: Windows 8 Native Apps
                // The type and name attributes are restricted during .innerHTML assignment
                var input = doc.createElement("input");
                input.setAttribute( "type", "hidden" );
                div.appendChild( input ).setAttribute( "name", "D" );
    
                // Support: IE8
                // Enforce case-sensitivity of name attribute
                if ( div.querySelectorAll("[name=d]").length ) {
                    rbuggyQSA.push( "name" + whitespace + "*[*^$|!~]?=" );
                }
    
                // FF 3.5 - :enabled/:disabled and hidden elements (hidden elements are still enabled)
                // IE8 throws error here and will not see later tests
                if ( !div.querySelectorAll(":enabled").length ) {
                    rbuggyQSA.push( ":enabled", ":disabled" );
                }
    
                // Opera 10-11 does not throw on post-comma invalid pseudos
                div.querySelectorAll("*,:x");
                rbuggyQSA.push(",.*:");
            });
        }
    
        if ( (support.matchesSelector = rnative.test( (matches = docElem.matches ||
            docElem.webkitMatchesSelector ||
            docElem.mozMatchesSelector ||
            docElem.oMatchesSelector ||
            docElem.msMatchesSelector) )) ) {
    
            assert(function( div ) {
                // Check to see if it's possible to do matchesSelector
                // on a disconnected node (IE 9)
                support.disconnectedMatch = matches.call( div, "div" );
    
                // This should fail with an exception
                // Gecko does not error, returns false instead
                matches.call( div, "[s!='']:x" );
                rbuggyMatches.push( "!=", pseudos );
            });
        }
    
        rbuggyQSA = rbuggyQSA.length && new RegExp( rbuggyQSA.join("|") );
        rbuggyMatches = rbuggyMatches.length && new RegExp( rbuggyMatches.join("|") );
    
        /* Contains
        ---------------------------------------------------------------------- */
        hasCompare = rnative.test( docElem.compareDocumentPosition );
    
        // Element contains another
        // Purposefully does not implement inclusive descendent
        // As in, an element does not contain itself
        contains = hasCompare || rnative.test( docElem.contains ) ?
            function( a, b ) {
                var adown = a.nodeType === 9 ? a.documentElement : a,
                    bup = b && b.parentNode;
                return a === bup || !!( bup && bup.nodeType === 1 && (
                    adown.contains ?
                        adown.contains( bup ) :
                        a.compareDocumentPosition && a.compareDocumentPosition( bup ) & 16
                ));
            } :
            function( a, b ) {
                if ( b ) {
                    while ( (b = b.parentNode) ) {
                        if ( b === a ) {
                            return true;
                        }
                    }
                }
                return false;
            };
    
        /* Sorting
        ---------------------------------------------------------------------- */
    
        // Document order sorting
        sortOrder = hasCompare ?
        function( a, b ) {
    
            // Flag for duplicate removal
            if ( a === b ) {
                hasDuplicate = true;
                return 0;
            }
    
            // Sort on method existence if only one input has compareDocumentPosition
            var compare = !a.compareDocumentPosition - !b.compareDocumentPosition;
            if ( compare ) {
                return compare;
            }
    
            // Calculate position if both inputs belong to the same document
            compare = ( a.ownerDocument || a ) === ( b.ownerDocument || b ) ?
                a.compareDocumentPosition( b ) :
    
                // Otherwise we know they are disconnected
                1;
    
            // Disconnected nodes
            if ( compare & 1 ||
                (!support.sortDetached && b.compareDocumentPosition( a ) === compare) ) {
    
                // Choose the first element that is related to our preferred document
                if ( a === doc || a.ownerDocument === preferredDoc && contains(preferredDoc, a) ) {
                    return -1;
                }
                if ( b === doc || b.ownerDocument === preferredDoc && contains(preferredDoc, b) ) {
                    return 1;
                }
    
                // Maintain original order
                return sortInput ?
                    ( indexOf.call( sortInput, a ) - indexOf.call( sortInput, b ) ) :
                    0;
            }
    
            return compare & 4 ? -1 : 1;
        } :
        function( a, b ) {
            // Exit early if the nodes are identical
            if ( a === b ) {
                hasDuplicate = true;
                return 0;
            }
    
            var cur,
                i = 0,
                aup = a.parentNode,
                bup = b.parentNode,
                ap = [ a ],
                bp = [ b ];
    
            // Parentless nodes are either documents or disconnected
            if ( !aup || !bup ) {
                return a === doc ? -1 :
                    b === doc ? 1 :
                    aup ? -1 :
                    bup ? 1 :
                    sortInput ?
                    ( indexOf.call( sortInput, a ) - indexOf.call( sortInput, b ) ) :
                    0;
    
            // If the nodes are siblings, we can do a quick check
            } else if ( aup === bup ) {
                return siblingCheck( a, b );
            }
    
            // Otherwise we need full lists of their ancestors for comparison
            cur = a;
            while ( (cur = cur.parentNode) ) {
                ap.unshift( cur );
            }
            cur = b;
            while ( (cur = cur.parentNode) ) {
                bp.unshift( cur );
            }
    
            // Walk down the tree looking for a discrepancy
            while ( ap[i] === bp[i] ) {
                i++;
            }
    
            return i ?
                // Do a sibling check if the nodes have a common ancestor
                siblingCheck( ap[i], bp[i] ) :
    
                // Otherwise nodes in our document sort first
                ap[i] === preferredDoc ? -1 :
                bp[i] === preferredDoc ? 1 :
                0;
        };
    
        return doc;
    };
    
    Sizzle.matches = function( expr, elements ) {
        return Sizzle( expr, null, null, elements );
    };
    
    Sizzle.matchesSelector = function( elem, expr ) {
        // Set document vars if needed
        if ( ( elem.ownerDocument || elem ) !== document ) {
            setDocument( elem );
        }
    
        // Make sure that attribute selectors are quoted
        expr = expr.replace( rattributeQuotes, "='$1']" );
    
        if ( support.matchesSelector && documentIsHTML &&
            ( !rbuggyMatches || !rbuggyMatches.test( expr ) ) &&
            ( !rbuggyQSA     || !rbuggyQSA.test( expr ) ) ) {
    
            try {
                var ret = matches.call( elem, expr );
    
                // IE 9's matchesSelector returns false on disconnected nodes
                if ( ret || support.disconnectedMatch ||
                        // As well, disconnected nodes are said to be in a document
                        // fragment in IE 9
                        elem.document && elem.document.nodeType !== 11 ) {
                    return ret;
                }
            } catch(e) {}
        }
    
        return Sizzle( expr, document, null, [ elem ] ).length > 0;
    };
    
    Sizzle.contains = function( context, elem ) {
        // Set document vars if needed
        if ( ( context.ownerDocument || context ) !== document ) {
            setDocument( context );
        }
        return contains( context, elem );
    };
    
    Sizzle.attr = function( elem, name ) {
        // Set document vars if needed
        if ( ( elem.ownerDocument || elem ) !== document ) {
            setDocument( elem );
        }
    
        var fn = Expr.attrHandle[ name.toLowerCase() ],
            // Don't get fooled by Object.prototype properties (jQuery #13807)
            val = fn && hasOwn.call( Expr.attrHandle, name.toLowerCase() ) ?
                fn( elem, name, !documentIsHTML ) :
                undefined;
    
        return val !== undefined ?
            val :
            support.attributes || !documentIsHTML ?
                elem.getAttribute( name ) :
                (val = elem.getAttributeNode(name)) && val.specified ?
                    val.value :
                    null;
    };
    
    Sizzle.error = function( msg ) {
        throw new Error( "Syntax error, unrecognized expression: " + msg );
    };
    
    /**
     * Document sorting and removing duplicates
     * @param {ArrayLike} results
     */
    Sizzle.uniqueSort = function( results ) {
        var elem,
            duplicates = [],
            j = 0,
            i = 0;
    
        // Unless we *know* we can detect duplicates, assume their presence
        hasDuplicate = !support.detectDuplicates;
        sortInput = !support.sortStable && results.slice( 0 );
        results.sort( sortOrder );
    
        if ( hasDuplicate ) {
            while ( (elem = results[i++]) ) {
                if ( elem === results[ i ] ) {
                    j = duplicates.push( i );
                }
            }
            while ( j-- ) {
                results.splice( duplicates[ j ], 1 );
            }
        }
    
        // Clear input after sorting to release objects
        // See https://github.com/jquery/sizzle/pull/225
        sortInput = null;
    
        return results;
    };
    
    /**
     * Utility function for retrieving the text value of an array of DOM nodes
     * @param {Array|Element} elem
     */
    getText = Sizzle.getText = function( elem ) {
        var node,
            ret = "",
            i = 0,
            nodeType = elem.nodeType;
    
        if ( !nodeType ) {
            // If no nodeType, this is expected to be an array
            while ( (node = elem[i++]) ) {
                // Do not traverse comment nodes
                ret += getText( node );
            }
        } else if ( nodeType === 1 || nodeType === 9 || nodeType === 11 ) {
            // Use textContent for elements
            // innerText usage removed for consistency of new lines (jQuery #11153)
            if ( typeof elem.textContent === "string" ) {
                return elem.textContent;
            } else {
                // Traverse its children
                for ( elem = elem.firstChild; elem; elem = elem.nextSibling ) {
                    ret += getText( elem );
                }
            }
        } else if ( nodeType === 3 || nodeType === 4 ) {
            return elem.nodeValue;
        }
        // Do not include comment or processing instruction nodes
    
        return ret;
    };
    
    Expr = Sizzle.selectors = {
    
        // Can be adjusted by the user
        cacheLength: 50,
    
        createPseudo: markFunction,
    
        match: matchExpr,
    
        attrHandle: {},
    
        find: {},
    
        relative: {
            ">": { dir: "parentNode", first: true },
            " ": { dir: "parentNode" },
            "+": { dir: "previousSibling", first: true },
            "~": { dir: "previousSibling" }
        },
    
        preFilter: {
            "ATTR": function( match ) {
                match[1] = match[1].replace( runescape, funescape );
    
                // Move the given value to match[3] whether quoted or unquoted
                match[3] = ( match[3] || match[4] || match[5] || "" ).replace( runescape, funescape );
    
                if ( match[2] === "~=" ) {
                    match[3] = " " + match[3] + " ";
                }
    
                return match.slice( 0, 4 );
            },
    
            "CHILD": function( match ) {
                /* matches from matchExpr["CHILD"]
                    1 type (only|nth|...)
                    2 what (child|of-type)
                    3 argument (even|odd|\d*|\d*n([+-]\d+)?|...)
                    4 xn-component of xn+y argument ([+-]?\d*n|)
                    5 sign of xn-component
                    6 x of xn-component
                    7 sign of y-component
                    8 y of y-component
                */
                match[1] = match[1].toLowerCase();
    
                if ( match[1].slice( 0, 3 ) === "nth" ) {
                    // nth-* requires argument
                    if ( !match[3] ) {
                        Sizzle.error( match[0] );
                    }
    
                    // numeric x and y parameters for Expr.filter.CHILD
                    // remember that false/true cast respectively to 0/1
                    match[4] = +( match[4] ? match[5] + (match[6] || 1) : 2 * ( match[3] === "even" || match[3] === "odd" ) );
                    match[5] = +( ( match[7] + match[8] ) || match[3] === "odd" );
    
                // other types prohibit arguments
                } else if ( match[3] ) {
                    Sizzle.error( match[0] );
                }
    
                return match;
            },
    
            "PSEUDO": function( match ) {
                var excess,
                    unquoted = !match[6] && match[2];
    
                if ( matchExpr["CHILD"].test( match[0] ) ) {
                    return null;
                }
    
                // Accept quoted arguments as-is
                if ( match[3] ) {
                    match[2] = match[4] || match[5] || "";
    
                // Strip excess characters from unquoted arguments
                } else if ( unquoted && rpseudo.test( unquoted ) &&
                    // Get excess from tokenize (recursively)
                    (excess = tokenize( unquoted, true )) &&
                    // advance to the next closing parenthesis
                    (excess = unquoted.indexOf( ")", unquoted.length - excess ) - unquoted.length) ) {
    
                    // excess is a negative index
                    match[0] = match[0].slice( 0, excess );
                    match[2] = unquoted.slice( 0, excess );
                }
    
                // Return only captures needed by the pseudo filter method (type and argument)
                return match.slice( 0, 3 );
            }
        },
    
        filter: {
    
            "TAG": function( nodeNameSelector ) {
                var nodeName = nodeNameSelector.replace( runescape, funescape ).toLowerCase();
                return nodeNameSelector === "*" ?
                    function() { return true; } :
                    function( elem ) {
                        return elem.nodeName && elem.nodeName.toLowerCase() === nodeName;
                    };
            },
    
            "CLASS": function( className ) {
                var pattern = classCache[ className + " " ];
    
                return pattern ||
                    (pattern = new RegExp( "(^|" + whitespace + ")" + className + "(" + whitespace + "|$)" )) &&
                    classCache( className, function( elem ) {
                        return pattern.test( typeof elem.className === "string" && elem.className || typeof elem.getAttribute !== strundefined && elem.getAttribute("class") || "" );
                    });
            },
    
            "ATTR": function( name, operator, check ) {
                return function( elem ) {
                    var result = Sizzle.attr( elem, name );
    
                    if ( result == null ) {
                        return operator === "!=";
                    }
                    if ( !operator ) {
                        return true;
                    }
    
                    result += "";
    
                    return operator === "=" ? result === check :
                        operator === "!=" ? result !== check :
                        operator === "^=" ? check && result.indexOf( check ) === 0 :
                        operator === "*=" ? check && result.indexOf( check ) > -1 :
                        operator === "$=" ? check && result.slice( -check.length ) === check :
                        operator === "~=" ? ( " " + result + " " ).indexOf( check ) > -1 :
                        operator === "|=" ? result === check || result.slice( 0, check.length + 1 ) === check + "-" :
                        false;
                };
            },
    
            "CHILD": function( type, what, argument, first, last ) {
                var simple = type.slice( 0, 3 ) !== "nth",
                    forward = type.slice( -4 ) !== "last",
                    ofType = what === "of-type";
    
                return first === 1 && last === 0 ?
    
                    // Shortcut for :nth-*(n)
                    function( elem ) {
                        return !!elem.parentNode;
                    } :
    
                    function( elem, context, xml ) {
                        var cache, outerCache, node, diff, nodeIndex, start,
                            dir = simple !== forward ? "nextSibling" : "previousSibling",
                            parent = elem.parentNode,
                            name = ofType && elem.nodeName.toLowerCase(),
                            useCache = !xml && !ofType;
    
                        if ( parent ) {
    
                            // :(first|last|only)-(child|of-type)
                            if ( simple ) {
                                while ( dir ) {
                                    node = elem;
                                    while ( (node = node[ dir ]) ) {
                                        if ( ofType ? node.nodeName.toLowerCase() === name : node.nodeType === 1 ) {
                                            return false;
                                        }
                                    }
                                    // Reverse direction for :only-* (if we haven't yet done so)
                                    start = dir = type === "only" && !start && "nextSibling";
                                }
                                return true;
                            }
    
                            start = [ forward ? parent.firstChild : parent.lastChild ];
    
                            // non-xml :nth-child(...) stores cache data on `parent`
                            if ( forward && useCache ) {
                                // Seek `elem` from a previously-cached index
                                outerCache = parent[ expando ] || (parent[ expando ] = {});
                                cache = outerCache[ type ] || [];
                                nodeIndex = cache[0] === dirruns && cache[1];
                                diff = cache[0] === dirruns && cache[2];
                                node = nodeIndex && parent.childNodes[ nodeIndex ];
    
                                while ( (node = ++nodeIndex && node && node[ dir ] ||
    
                                    // Fallback to seeking `elem` from the start
                                    (diff = nodeIndex = 0) || start.pop()) ) {
    
                                    // When found, cache indexes on `parent` and break
                                    if ( node.nodeType === 1 && ++diff && node === elem ) {
                                        outerCache[ type ] = [ dirruns, nodeIndex, diff ];
                                        break;
                                    }
                                }
    
                            // Use previously-cached element index if available
                            } else if ( useCache && (cache = (elem[ expando ] || (elem[ expando ] = {}))[ type ]) && cache[0] === dirruns ) {
                                diff = cache[1];
    
                            // xml :nth-child(...) or :nth-last-child(...) or :nth(-last)?-of-type(...)
                            } else {
                                // Use the same loop as above to seek `elem` from the start
                                while ( (node = ++nodeIndex && node && node[ dir ] ||
                                    (diff = nodeIndex = 0) || start.pop()) ) {
    
                                    if ( ( ofType ? node.nodeName.toLowerCase() === name : node.nodeType === 1 ) && ++diff ) {
                                        // Cache the index of each encountered element
                                        if ( useCache ) {
                                            (node[ expando ] || (node[ expando ] = {}))[ type ] = [ dirruns, diff ];
                                        }
    
                                        if ( node === elem ) {
                                            break;
                                        }
                                    }
                                }
                            }
    
                            // Incorporate the offset, then check against cycle size
                            diff -= last;
                            return diff === first || ( diff % first === 0 && diff / first >= 0 );
                        }
                    };
            },
    
            "PSEUDO": function( pseudo, argument ) {
                // pseudo-class names are case-insensitive
                // http://www.w3.org/TR/selectors/#pseudo-classes
                // Prioritize by case sensitivity in case custom pseudos are added with uppercase letters
                // Remember that setFilters inherits from pseudos
                var args,
                    fn = Expr.pseudos[ pseudo ] || Expr.setFilters[ pseudo.toLowerCase() ] ||
                        Sizzle.error( "unsupported pseudo: " + pseudo );
    
                // The user may use createPseudo to indicate that
                // arguments are needed to create the filter function
                // just as Sizzle does
                if ( fn[ expando ] ) {
                    return fn( argument );
                }
    
                // But maintain support for old signatures
                if ( fn.length > 1 ) {
                    args = [ pseudo, pseudo, "", argument ];
                    return Expr.setFilters.hasOwnProperty( pseudo.toLowerCase() ) ?
                        markFunction(function( seed, matches ) {
                            var idx,
                                matched = fn( seed, argument ),
                                i = matched.length;
                            while ( i-- ) {
                                idx = indexOf.call( seed, matched[i] );
                                seed[ idx ] = !( matches[ idx ] = matched[i] );
                            }
                        }) :
                        function( elem ) {
                            return fn( elem, 0, args );
                        };
                }
    
                return fn;
            }
        },
    
        pseudos: {
            // Potentially complex pseudos
            "not": markFunction(function( selector ) {
                // Trim the selector passed to compile
                // to avoid treating leading and trailing
                // spaces as combinators
                var input = [],
                    results = [],
                    matcher = compile( selector.replace( rtrim, "$1" ) );
    
                return matcher[ expando ] ?
                    markFunction(function( seed, matches, context, xml ) {
                        var elem,
                            unmatched = matcher( seed, null, xml, [] ),
                            i = seed.length;
    
                        // Match elements unmatched by `matcher`
                        while ( i-- ) {
                            if ( (elem = unmatched[i]) ) {
                                seed[i] = !(matches[i] = elem);
                            }
                        }
                    }) :
                    function( elem, context, xml ) {
                        input[0] = elem;
                        matcher( input, null, xml, results );
                        return !results.pop();
                    };
            }),
    
            "has": markFunction(function( selector ) {
                return function( elem ) {
                    return Sizzle( selector, elem ).length > 0;
                };
            }),
    
            "contains": markFunction(function( text ) {
                return function( elem ) {
                    return ( elem.textContent || elem.innerText || getText( elem ) ).indexOf( text ) > -1;
                };
            }),
    
            // "Whether an element is represented by a :lang() selector
            // is based solely on the element's language value
            // being equal to the identifier C,
            // or beginning with the identifier C immediately followed by "-".
            // The matching of C against the element's language value is performed case-insensitively.
            // The identifier C does not have to be a valid language name."
            // http://www.w3.org/TR/selectors/#lang-pseudo
            "lang": markFunction( function( lang ) {
                // lang value must be a valid identifier
                if ( !ridentifier.test(lang || "") ) {
                    Sizzle.error( "unsupported lang: " + lang );
                }
                lang = lang.replace( runescape, funescape ).toLowerCase();
                return function( elem ) {
                    var elemLang;
                    do {
                        if ( (elemLang = documentIsHTML ?
                            elem.lang :
                            elem.getAttribute("xml:lang") || elem.getAttribute("lang")) ) {
    
                            elemLang = elemLang.toLowerCase();
                            return elemLang === lang || elemLang.indexOf( lang + "-" ) === 0;
                        }
                    } while ( (elem = elem.parentNode) && elem.nodeType === 1 );
                    return false;
                };
            }),
    
            // Miscellaneous
            "target": function( elem ) {
                var hash = window.location && window.location.hash;
                return hash && hash.slice( 1 ) === elem.id;
            },
    
            "root": function( elem ) {
                return elem === docElem;
            },
    
            "focus": function( elem ) {
                return elem === document.activeElement && (!document.hasFocus || document.hasFocus()) && !!(elem.type || elem.href || ~elem.tabIndex);
            },
    
            // Boolean properties
            "enabled": function( elem ) {
                return elem.disabled === false;
            },
    
            "disabled": function( elem ) {
                return elem.disabled === true;
            },
    
            "checked": function( elem ) {
                // In CSS3, :checked should return both checked and selected elements
                // http://www.w3.org/TR/2011/REC-css3-selectors-20110929/#checked
                var nodeName = elem.nodeName.toLowerCase();
                return (nodeName === "input" && !!elem.checked) || (nodeName === "option" && !!elem.selected);
            },
    
            "selected": function( elem ) {
                // Accessing this property makes selected-by-default
                // options in Safari work properly
                if ( elem.parentNode ) {
                    elem.parentNode.selectedIndex;
                }
    
                return elem.selected === true;
            },
    
            // Contents
            "empty": function( elem ) {
                // http://www.w3.org/TR/selectors/#empty-pseudo
                // :empty is negated by element (1) or content nodes (text: 3; cdata: 4; entity ref: 5),
                //   but not by others (comment: 8; processing instruction: 7; etc.)
                // nodeType < 6 works because attributes (2) do not appear as children
                for ( elem = elem.firstChild; elem; elem = elem.nextSibling ) {
                    if ( elem.nodeType < 6 ) {
                        return false;
                    }
                }
                return true;
            },
    
            "parent": function( elem ) {
                return !Expr.pseudos["empty"]( elem );
            },
    
            // Element/input types
            "header": function( elem ) {
                return rheader.test( elem.nodeName );
            },
    
            "input": function( elem ) {
                return rinputs.test( elem.nodeName );
            },
    
            "button": function( elem ) {
                var name = elem.nodeName.toLowerCase();
                return name === "input" && elem.type === "button" || name === "button";
            },
    
            "text": function( elem ) {
                var attr;
                return elem.nodeName.toLowerCase() === "input" &&
                    elem.type === "text" &&
    
                    // Support: IE<8
                    // New HTML5 attribute values (e.g., "search") appear with elem.type === "text"
                    ( (attr = elem.getAttribute("type")) == null || attr.toLowerCase() === "text" );
            },
    
            // Position-in-collection
            "first": createPositionalPseudo(function() {
                return [ 0 ];
            }),
    
            "last": createPositionalPseudo(function( matchIndexes, length ) {
                return [ length - 1 ];
            }),
    
            "eq": createPositionalPseudo(function( matchIndexes, length, argument ) {
                return [ argument < 0 ? argument + length : argument ];
            }),
    
            "even": createPositionalPseudo(function( matchIndexes, length ) {
                var i = 0;
                for ( ; i < length; i += 2 ) {
                    matchIndexes.push( i );
                }
                return matchIndexes;
            }),
    
            "odd": createPositionalPseudo(function( matchIndexes, length ) {
                var i = 1;
                for ( ; i < length; i += 2 ) {
                    matchIndexes.push( i );
                }
                return matchIndexes;
            }),
    
            "lt": createPositionalPseudo(function( matchIndexes, length, argument ) {
                var i = argument < 0 ? argument + length : argument;
                for ( ; --i >= 0; ) {
                    matchIndexes.push( i );
                }
                return matchIndexes;
            }),
    
            "gt": createPositionalPseudo(function( matchIndexes, length, argument ) {
                var i = argument < 0 ? argument + length : argument;
                for ( ; ++i < length; ) {
                    matchIndexes.push( i );
                }
                return matchIndexes;
            })
        }
    };
    
    Expr.pseudos["nth"] = Expr.pseudos["eq"];
    
    // Add button/input type pseudos
    for ( i in { radio: true, checkbox: true, file: true, password: true, image: true } ) {
        Expr.pseudos[ i ] = createInputPseudo( i );
    }
    for ( i in { submit: true, reset: true } ) {
        Expr.pseudos[ i ] = createButtonPseudo( i );
    }
    
    // Easy API for creating new setFilters
    function setFilters() {}
    setFilters.prototype = Expr.filters = Expr.pseudos;
    Expr.setFilters = new setFilters();
    
    tokenize = Sizzle.tokenize = function( selector, parseOnly ) {
        var matched, match, tokens, type,
            soFar, groups, preFilters,
            cached = tokenCache[ selector + " " ];
    
        if ( cached ) {
            return parseOnly ? 0 : cached.slice( 0 );
        }
    
        soFar = selector;
        groups = [];
        preFilters = Expr.preFilter;
    
        while ( soFar ) {
    
            // Comma and first run
            if ( !matched || (match = rcomma.exec( soFar )) ) {
                if ( match ) {
                    // Don't consume trailing commas as valid
                    soFar = soFar.slice( match[0].length ) || soFar;
                }
                groups.push( (tokens = []) );
            }
    
            matched = false;
    
            // Combinators
            if ( (match = rcombinators.exec( soFar )) ) {
                matched = match.shift();
                tokens.push({
                    value: matched,
                    // Cast descendant combinators to space
                    type: match[0].replace( rtrim, " " )
                });
                soFar = soFar.slice( matched.length );
            }
    
            // Filters
            for ( type in Expr.filter ) {
                if ( (match = matchExpr[ type ].exec( soFar )) && (!preFilters[ type ] ||
                    (match = preFilters[ type ]( match ))) ) {
                    matched = match.shift();
                    tokens.push({
                        value: matched,
                        type: type,
                        matches: match
                    });
                    soFar = soFar.slice( matched.length );
                }
            }
    
            if ( !matched ) {
                break;
            }
        }
    
        // Return the length of the invalid excess
        // if we're just parsing
        // Otherwise, throw an error or return tokens
        return parseOnly ?
            soFar.length :
            soFar ?
                Sizzle.error( selector ) :
                // Cache the tokens
                tokenCache( selector, groups ).slice( 0 );
    };
    
    function toSelector( tokens ) {
        var i = 0,
            len = tokens.length,
            selector = "";
        for ( ; i < len; i++ ) {
            selector += tokens[i].value;
        }
        return selector;
    }
    
    function addCombinator( matcher, combinator, base ) {
        var dir = combinator.dir,
            checkNonElements = base && dir === "parentNode",
            doneName = done++;
    
        return combinator.first ?
            // Check against closest ancestor/preceding element
            function( elem, context, xml ) {
                while ( (elem = elem[ dir ]) ) {
                    if ( elem.nodeType === 1 || checkNonElements ) {
                        return matcher( elem, context, xml );
                    }
                }
            } :
    
            // Check against all ancestor/preceding elements
            function( elem, context, xml ) {
                var oldCache, outerCache,
                    newCache = [ dirruns, doneName ];
    
                // We can't set arbitrary data on XML nodes, so they don't benefit from dir caching
                if ( xml ) {
                    while ( (elem = elem[ dir ]) ) {
                        if ( elem.nodeType === 1 || checkNonElements ) {
                            if ( matcher( elem, context, xml ) ) {
                                return true;
                            }
                        }
                    }
                } else {
                    while ( (elem = elem[ dir ]) ) {
                        if ( elem.nodeType === 1 || checkNonElements ) {
                            outerCache = elem[ expando ] || (elem[ expando ] = {});
                            if ( (oldCache = outerCache[ dir ]) &&
                                oldCache[ 0 ] === dirruns && oldCache[ 1 ] === doneName ) {
    
                                // Assign to newCache so results back-propagate to previous elements
                                return (newCache[ 2 ] = oldCache[ 2 ]);
                            } else {
                                // Reuse newcache so results back-propagate to previous elements
                                outerCache[ dir ] = newCache;
    
                                // A match means we're done; a fail means we have to keep checking
                                if ( (newCache[ 2 ] = matcher( elem, context, xml )) ) {
                                    return true;
                                }
                            }
                        }
                    }
                }
            };
    }
    
    function elementMatcher( matchers ) {
        return matchers.length > 1 ?
            function( elem, context, xml ) {
                var i = matchers.length;
                while ( i-- ) {
                    if ( !matchers[i]( elem, context, xml ) ) {
                        return false;
                    }
                }
                return true;
            } :
            matchers[0];
    }
    
    function multipleContexts( selector, contexts, results ) {
        var i = 0,
            len = contexts.length;
        for ( ; i < len; i++ ) {
            Sizzle( selector, contexts[i], results );
        }
        return results;
    }
    
    function condense( unmatched, map, filter, context, xml ) {
        var elem,
            newUnmatched = [],
            i = 0,
            len = unmatched.length,
            mapped = map != null;
    
        for ( ; i < len; i++ ) {
            if ( (elem = unmatched[i]) ) {
                if ( !filter || filter( elem, context, xml ) ) {
                    newUnmatched.push( elem );
                    if ( mapped ) {
                        map.push( i );
                    }
                }
            }
        }
    
        return newUnmatched;
    }
    
    function setMatcher( preFilter, selector, matcher, postFilter, postFinder, postSelector ) {
        if ( postFilter && !postFilter[ expando ] ) {
            postFilter = setMatcher( postFilter );
        }
        if ( postFinder && !postFinder[ expando ] ) {
            postFinder = setMatcher( postFinder, postSelector );
        }
        return markFunction(function( seed, results, context, xml ) {
            var temp, i, elem,
                preMap = [],
                postMap = [],
                preexisting = results.length,
    
                // Get initial elements from seed or context
                elems = seed || multipleContexts( selector || "*", context.nodeType ? [ context ] : context, [] ),
    
                // Prefilter to get matcher input, preserving a map for seed-results synchronization
                matcherIn = preFilter && ( seed || !selector ) ?
                    condense( elems, preMap, preFilter, context, xml ) :
                    elems,
    
                matcherOut = matcher ?
                    // If we have a postFinder, or filtered seed, or non-seed postFilter or preexisting results,
                    postFinder || ( seed ? preFilter : preexisting || postFilter ) ?
    
                        // ...intermediate processing is necessary
                        [] :
    
                        // ...otherwise use results directly
                        results :
                    matcherIn;
    
            // Find primary matches
            if ( matcher ) {
                matcher( matcherIn, matcherOut, context, xml );
            }
    
            // Apply postFilter
            if ( postFilter ) {
                temp = condense( matcherOut, postMap );
                postFilter( temp, [], context, xml );
    
                // Un-match failing elements by moving them back to matcherIn
                i = temp.length;
                while ( i-- ) {
                    if ( (elem = temp[i]) ) {
                        matcherOut[ postMap[i] ] = !(matcherIn[ postMap[i] ] = elem);
                    }
                }
            }
    
            if ( seed ) {
                if ( postFinder || preFilter ) {
                    if ( postFinder ) {
                        // Get the final matcherOut by condensing this intermediate into postFinder contexts
                        temp = [];
                        i = matcherOut.length;
                        while ( i-- ) {
                            if ( (elem = matcherOut[i]) ) {
                                // Restore matcherIn since elem is not yet a final match
                                temp.push( (matcherIn[i] = elem) );
                            }
                        }
                        postFinder( null, (matcherOut = []), temp, xml );
                    }
    
                    // Move matched elements from seed to results to keep them synchronized
                    i = matcherOut.length;
                    while ( i-- ) {
                        if ( (elem = matcherOut[i]) &&
                            (temp = postFinder ? indexOf.call( seed, elem ) : preMap[i]) > -1 ) {
    
                            seed[temp] = !(results[temp] = elem);
                        }
                    }
                }
    
            // Add elements to results, through postFinder if defined
            } else {
                matcherOut = condense(
                    matcherOut === results ?
                        matcherOut.splice( preexisting, matcherOut.length ) :
                        matcherOut
                );
                if ( postFinder ) {
                    postFinder( null, results, matcherOut, xml );
                } else {
                    push.apply( results, matcherOut );
                }
            }
        });
    }
    
    function matcherFromTokens( tokens ) {
        var checkContext, matcher, j,
            len = tokens.length,
            leadingRelative = Expr.relative[ tokens[0].type ],
            implicitRelative = leadingRelative || Expr.relative[" "],
            i = leadingRelative ? 1 : 0,
    
            // The foundational matcher ensures that elements are reachable from top-level context(s)
            matchContext = addCombinator( function( elem ) {
                return elem === checkContext;
            }, implicitRelative, true ),
            matchAnyContext = addCombinator( function( elem ) {
                return indexOf.call( checkContext, elem ) > -1;
            }, implicitRelative, true ),
            matchers = [ function( elem, context, xml ) {
                return ( !leadingRelative && ( xml || context !== outermostContext ) ) || (
                    (checkContext = context).nodeType ?
                        matchContext( elem, context, xml ) :
                        matchAnyContext( elem, context, xml ) );
            } ];
    
        for ( ; i < len; i++ ) {
            if ( (matcher = Expr.relative[ tokens[i].type ]) ) {
                matchers = [ addCombinator(elementMatcher( matchers ), matcher) ];
            } else {
                matcher = Expr.filter[ tokens[i].type ].apply( null, tokens[i].matches );
    
                // Return special upon seeing a positional matcher
                if ( matcher[ expando ] ) {
                    // Find the next relative operator (if any) for proper handling
                    j = ++i;
                    for ( ; j < len; j++ ) {
                        if ( Expr.relative[ tokens[j].type ] ) {
                            break;
                        }
                    }
                    return setMatcher(
                        i > 1 && elementMatcher( matchers ),
                        i > 1 && toSelector(
                            // If the preceding token was a descendant combinator, insert an implicit any-element `*`
                            tokens.slice( 0, i - 1 ).concat({ value: tokens[ i - 2 ].type === " " ? "*" : "" })
                        ).replace( rtrim, "$1" ),
                        matcher,
                        i < j && matcherFromTokens( tokens.slice( i, j ) ),
                        j < len && matcherFromTokens( (tokens = tokens.slice( j )) ),
                        j < len && toSelector( tokens )
                    );
                }
                matchers.push( matcher );
            }
        }
    
        return elementMatcher( matchers );
    }
    
    function matcherFromGroupMatchers( elementMatchers, setMatchers ) {
        var bySet = setMatchers.length > 0,
            byElement = elementMatchers.length > 0,
            superMatcher = function( seed, context, xml, results, outermost ) {
                var elem, j, matcher,
                    matchedCount = 0,
                    i = "0",
                    unmatched = seed && [],
                    setMatched = [],
                    contextBackup = outermostContext,
                    // We must always have either seed elements or outermost context
                    elems = seed || byElement && Expr.find["TAG"]( "*", outermost ),
                    // Use integer dirruns iff this is the outermost matcher
                    dirrunsUnique = (dirruns += contextBackup == null ? 1 : Math.random() || 0.1),
                    len = elems.length;
    
                if ( outermost ) {
                    outermostContext = context !== document && context;
                }
    
                // Add elements passing elementMatchers directly to results
                // Keep `i` a string if there are no elements so `matchedCount` will be "00" below
                // Support: IE<9, Safari
                // Tolerate NodeList properties (IE: "length"; Safari: <number>) matching elements by id
                for ( ; i !== len && (elem = elems[i]) != null; i++ ) {
                    if ( byElement && elem ) {
                        j = 0;
                        while ( (matcher = elementMatchers[j++]) ) {
                            if ( matcher( elem, context, xml ) ) {
                                results.push( elem );
                                break;
                            }
                        }
                        if ( outermost ) {
                            dirruns = dirrunsUnique;
                        }
                    }
    
                    // Track unmatched elements for set filters
                    if ( bySet ) {
                        // They will have gone through all possible matchers
                        if ( (elem = !matcher && elem) ) {
                            matchedCount--;
                        }
    
                        // Lengthen the array for every element, matched or not
                        if ( seed ) {
                            unmatched.push( elem );
                        }
                    }
                }
    
                // Apply set filters to unmatched elements
                matchedCount += i;
                if ( bySet && i !== matchedCount ) {
                    j = 0;
                    while ( (matcher = setMatchers[j++]) ) {
                        matcher( unmatched, setMatched, context, xml );
                    }
    
                    if ( seed ) {
                        // Reintegrate element matches to eliminate the need for sorting
                        if ( matchedCount > 0 ) {
                            while ( i-- ) {
                                if ( !(unmatched[i] || setMatched[i]) ) {
                                    setMatched[i] = pop.call( results );
                                }
                            }
                        }
    
                        // Discard index placeholder values to get only actual matches
                        setMatched = condense( setMatched );
                    }
    
                    // Add matches to results
                    push.apply( results, setMatched );
    
                    // Seedless set matches succeeding multiple successful matchers stipulate sorting
                    if ( outermost && !seed && setMatched.length > 0 &&
                        ( matchedCount + setMatchers.length ) > 1 ) {
    
                        Sizzle.uniqueSort( results );
                    }
                }
    
                // Override manipulation of globals by nested matchers
                if ( outermost ) {
                    dirruns = dirrunsUnique;
                    outermostContext = contextBackup;
                }
    
                return unmatched;
            };
    
        return bySet ?
            markFunction( superMatcher ) :
            superMatcher;
    }
    
    compile = Sizzle.compile = function( selector, match /* Internal Use Only */ ) {
        var i,
            setMatchers = [],
            elementMatchers = [],
            cached = compilerCache[ selector + " " ];
    
        if ( !cached ) {
            // Generate a function of recursive functions that can be used to check each element
            if ( !match ) {
                match = tokenize( selector );
            }
            i = match.length;
            while ( i-- ) {
                cached = matcherFromTokens( match[i] );
                if ( cached[ expando ] ) {
                    setMatchers.push( cached );
                } else {
                    elementMatchers.push( cached );
                }
            }
    
            // Cache the compiled function
            cached = compilerCache( selector, matcherFromGroupMatchers( elementMatchers, setMatchers ) );
    
            // Save selector and tokenization
            cached.selector = selector;
        }
        return cached;
    };
    
    /**
     * A low-level selection function that works with Sizzle's compiled
     *  selector functions
     * @param {String|Function} selector A selector or a pre-compiled
     *  selector function built with Sizzle.compile
     * @param {Element} context
     * @param {Array} [results]
     * @param {Array} [seed] A set of elements to match against
     */
    select = Sizzle.select = function( selector, context, results, seed ) {
        var i, tokens, token, type, find,
            compiled = typeof selector === "function" && selector,
            match = !seed && tokenize( (selector = compiled.selector || selector) );
    
        results = results || [];
    
        // Try to minimize operations if there is no seed and only one group
        if ( match.length === 1 ) {
    
            // Take a shortcut and set the context if the root selector is an ID
            tokens = match[0] = match[0].slice( 0 );
            if ( tokens.length > 2 && (token = tokens[0]).type === "ID" &&
                    support.getById && context.nodeType === 9 && documentIsHTML &&
                    Expr.relative[ tokens[1].type ] ) {
    
                context = ( Expr.find["ID"]( token.matches[0].replace(runescape, funescape), context ) || [] )[0];
                if ( !context ) {
                    return results;
    
                // Precompiled matchers will still verify ancestry, so step up a level
                } else if ( compiled ) {
                    context = context.parentNode;
                }
    
                selector = selector.slice( tokens.shift().value.length );
            }
    
            // Fetch a seed set for right-to-left matching
            i = matchExpr["needsContext"].test( selector ) ? 0 : tokens.length;
            while ( i-- ) {
                token = tokens[i];
    
                // Abort if we hit a combinator
                if ( Expr.relative[ (type = token.type) ] ) {
                    break;
                }
                if ( (find = Expr.find[ type ]) ) {
                    // Search, expanding context for leading sibling combinators
                    if ( (seed = find(
                        token.matches[0].replace( runescape, funescape ),
                        rsibling.test( tokens[0].type ) && testContext( context.parentNode ) || context
                    )) ) {
    
                        // If seed is empty or no tokens remain, we can return early
                        tokens.splice( i, 1 );
                        selector = seed.length && toSelector( tokens );
                        if ( !selector ) {
                            push.apply( results, seed );
                            return results;
                        }
    
                        break;
                    }
                }
            }
        }
    
        // Compile and execute a filtering function if one is not provided
        // Provide `match` to avoid retokenization if we modified the selector above
        ( compiled || compile( selector, match ) )(
            seed,
            context,
            !documentIsHTML,
            results,
            rsibling.test( selector ) && testContext( context.parentNode ) || context
        );
        return results;
    };
    
    // One-time assignments
    
    // Sort stability
    support.sortStable = expando.split("").sort( sortOrder ).join("") === expando;
    
    // Support: Chrome<14
    // Always assume duplicates if they aren't passed to the comparison function
    support.detectDuplicates = !!hasDuplicate;
    
    // Initialize against the default document
    setDocument();
    
    // Support: Webkit<537.32 - Safari 6.0.3/Chrome 25 (fixed in Chrome 27)
    // Detached nodes confoundingly follow *each other*
    support.sortDetached = assert(function( div1 ) {
        // Should return 1, but returns 4 (following)
        return div1.compareDocumentPosition( document.createElement("div") ) & 1;
    });
    
    // Support: IE<8
    // Prevent attribute/property "interpolation"
    // http://msdn.microsoft.com/en-us/library/ms536429%28VS.85%29.aspx
    if ( !assert(function( div ) {
        div.innerHTML = "<a href='#'></a>";
        return div.firstChild.getAttribute("href") === "#" ;
    }) ) {
        addHandle( "type|href|height|width", function( elem, name, isXML ) {
            if ( !isXML ) {
                return elem.getAttribute( name, name.toLowerCase() === "type" ? 1 : 2 );
            }
        });
    }
    
    // Support: IE<9
    // Use defaultValue in place of getAttribute("value")
    if ( !support.attributes || !assert(function( div ) {
        div.innerHTML = "<input/>";
        div.firstChild.setAttribute( "value", "" );
        return div.firstChild.getAttribute( "value" ) === "";
    }) ) {
        addHandle( "value", function( elem, name, isXML ) {
            if ( !isXML && elem.nodeName.toLowerCase() === "input" ) {
                return elem.defaultValue;
            }
        });
    }
    
    // Support: IE<9
    // Use getAttributeNode to fetch booleans when getAttribute lies
    if ( !assert(function( div ) {
        return div.getAttribute("disabled") == null;
    }) ) {
        addHandle( booleans, function( elem, name, isXML ) {
            var val;
            if ( !isXML ) {
                return elem[ name ] === true ? name.toLowerCase() :
                        (val = elem.getAttributeNode( name )) && val.specified ?
                        val.value :
                    null;
            }
        });
    }
    
    return Sizzle;
    
    })( window );
    
    
    
    jQuery.find = Sizzle;
    jQuery.expr = Sizzle.selectors;
    jQuery.expr[":"] = jQuery.expr.pseudos;
    jQuery.unique = Sizzle.uniqueSort;
    jQuery.text = Sizzle.getText;
    jQuery.isXMLDoc = Sizzle.isXML;
    jQuery.contains = Sizzle.contains;
    
    
    
    var rneedsContext = jQuery.expr.match.needsContext;
    
    var rsingleTag = (/^<(\w+)\s*\/?>(?:<\/\1>|)$/);
    
    
    
    var risSimple = /^.[^:#\[\.,]*$/;
    
    // Implement the identical functionality for filter and not
    function winnow( elements, qualifier, not ) {
        if ( jQuery.isFunction( qualifier ) ) {
            return jQuery.grep( elements, function( elem, i ) {
                /* jshint -W018 */
                return !!qualifier.call( elem, i, elem ) !== not;
            });
    
        }
    
        if ( qualifier.nodeType ) {
            return jQuery.grep( elements, function( elem ) {
                return ( elem === qualifier ) !== not;
            });
    
        }
    
        if ( typeof qualifier === "string" ) {
            if ( risSimple.test( qualifier ) ) {
                return jQuery.filter( qualifier, elements, not );
            }
    
            qualifier = jQuery.filter( qualifier, elements );
        }
    
        return jQuery.grep( elements, function( elem ) {
            return ( indexOf.call( qualifier, elem ) >= 0 ) !== not;
        });
    }
    
    jQuery.filter = function( expr, elems, not ) {
        var elem = elems[ 0 ];
    
        if ( not ) {
            expr = ":not(" + expr + ")";
        }
    
        return elems.length === 1 && elem.nodeType === 1 ?
            jQuery.find.matchesSelector( elem, expr ) ? [ elem ] : [] :
            jQuery.find.matches( expr, jQuery.grep( elems, function( elem ) {
                return elem.nodeType === 1;
            }));
    };
    
    jQuery.fn.extend({
        find: function( selector ) {
            var i,
                len = this.length,
                ret = [],
                self = this;
    
            if ( typeof selector !== "string" ) {
                return this.pushStack( jQuery( selector ).filter(function() {
                    for ( i = 0; i < len; i++ ) {
                        if ( jQuery.contains( self[ i ], this ) ) {
                            return true;
                        }
                    }
                }) );
            }
    
            for ( i = 0; i < len; i++ ) {
                jQuery.find( selector, self[ i ], ret );
            }
    
            // Needed because $( selector, context ) becomes $( context ).find( selector )
            ret = this.pushStack( len > 1 ? jQuery.unique( ret ) : ret );
            ret.selector = this.selector ? this.selector + " " + selector : selector;
            return ret;
        },
        filter: function( selector ) {
            return this.pushStack( winnow(this, selector || [], false) );
        },
        not: function( selector ) {
            return this.pushStack( winnow(this, selector || [], true) );
        },
        is: function( selector ) {
            return !!winnow(
                this,
    
                // If this is a positional/relative selector, check membership in the returned set
                // so $("p:first").is("p:last") won't return true for a doc with two "p".
                typeof selector === "string" && rneedsContext.test( selector ) ?
                    jQuery( selector ) :
                    selector || [],
                false
            ).length;
        }
    });
    
    
    // Initialize a jQuery object
    
    
    // A central reference to the root jQuery(document)
    var rootjQuery,
    
        // A simple way to check for HTML strings
        // Prioritize #id over <tag> to avoid XSS via location.hash (#9521)
        // Strict HTML recognition (#11290: must start with <)
        rquickExpr = /^(?:\s*(<[\w\W]+>)[^>]*|#([\w-]*))$/,
    
        init = jQuery.fn.init = function( selector, context ) {
            var match, elem;
    
            // HANDLE: $(""), $(null), $(undefined), $(false)
            if ( !selector ) {
                return this;
            }
    
            // Handle HTML strings
            if ( typeof selector === "string" ) {
                if ( selector[0] === "<" && selector[ selector.length - 1 ] === ">" && selector.length >= 3 ) {
                    // Assume that strings that start and end with <> are HTML and skip the regex check
                    match = [ null, selector, null ];
    
                } else {
                    match = rquickExpr.exec( selector );
                }
    
                // Match html or make sure no context is specified for #id
                if ( match && (match[1] || !context) ) {
    
                    // HANDLE: $(html) -> $(array)
                    if ( match[1] ) {
                        context = context instanceof jQuery ? context[0] : context;
    
                        // scripts is true for back-compat
                        // Intentionally let the error be thrown if parseHTML is not present
                        jQuery.merge( this, jQuery.parseHTML(
                            match[1],
                            context && context.nodeType ? context.ownerDocument || context : document,
                            true
                        ) );
    
                        // HANDLE: $(html, props)
                        if ( rsingleTag.test( match[1] ) && jQuery.isPlainObject( context ) ) {
                            for ( match in context ) {
                                // Properties of context are called as methods if possible
                                if ( jQuery.isFunction( this[ match ] ) ) {
                                    this[ match ]( context[ match ] );
    
                                // ...and otherwise set as attributes
                                } else {
                                    this.attr( match, context[ match ] );
                                }
                            }
                        }
    
                        return this;
    
                    // HANDLE: $(#id)
                    } else {
                        elem = document.getElementById( match[2] );
    
                        // Check parentNode to catch when Blackberry 4.6 returns
                        // nodes that are no longer in the document #6963
                        if ( elem && elem.parentNode ) {
                            // Inject the element directly into the jQuery object
                            this.length = 1;
                            this[0] = elem;
                        }
    
                        this.context = document;
                        this.selector = selector;
                        return this;
                    }
    
                // HANDLE: $(expr, $(...))
                } else if ( !context || context.jquery ) {
                    return ( context || rootjQuery ).find( selector );
    
                // HANDLE: $(expr, context)
                // (which is just equivalent to: $(context).find(expr)
                } else {
                    return this.constructor( context ).find( selector );
                }
    
            // HANDLE: $(DOMElement)
            } else if ( selector.nodeType ) {
                this.context = this[0] = selector;
                this.length = 1;
                return this;
    
            // HANDLE: $(function)
            // Shortcut for document ready
            } else if ( jQuery.isFunction( selector ) ) {
                return typeof rootjQuery.ready !== "undefined" ?
                    rootjQuery.ready( selector ) :
                    // Execute immediately if ready is not present
                    selector( jQuery );
            }
    
            if ( selector.selector !== undefined ) {
                this.selector = selector.selector;
                this.context = selector.context;
            }
    
            return jQuery.makeArray( selector, this );
        };
    
    // Give the init function the jQuery prototype for later instantiation
    init.prototype = jQuery.fn;
    
    // Initialize central reference
    rootjQuery = jQuery( document );
    
    
    var rparentsprev = /^(?:parents|prev(?:Until|All))/,
        // methods guaranteed to produce a unique set when starting from a unique set
        guaranteedUnique = {
            children: true,
            contents: true,
            next: true,
            prev: true
        };
    
    jQuery.extend({
        dir: function( elem, dir, until ) {
            var matched = [],
                truncate = until !== undefined;
    
            while ( (elem = elem[ dir ]) && elem.nodeType !== 9 ) {
                if ( elem.nodeType === 1 ) {
                    if ( truncate && jQuery( elem ).is( until ) ) {
                        break;
                    }
                    matched.push( elem );
                }
            }
            return matched;
        },
    
        sibling: function( n, elem ) {
            var matched = [];
    
            for ( ; n; n = n.nextSibling ) {
                if ( n.nodeType === 1 && n !== elem ) {
                    matched.push( n );
                }
            }
    
            return matched;
        }
    });
    
    jQuery.fn.extend({
        has: function( target ) {
            var targets = jQuery( target, this ),
                l = targets.length;
    
            return this.filter(function() {
                var i = 0;
                for ( ; i < l; i++ ) {
                    if ( jQuery.contains( this, targets[i] ) ) {
                        return true;
                    }
                }
            });
        },
    
        closest: function( selectors, context ) {
            var cur,
                i = 0,
                l = this.length,
                matched = [],
                pos = rneedsContext.test( selectors ) || typeof selectors !== "string" ?
                    jQuery( selectors, context || this.context ) :
                    0;
    
            for ( ; i < l; i++ ) {
                for ( cur = this[i]; cur && cur !== context; cur = cur.parentNode ) {
                    // Always skip document fragments
                    if ( cur.nodeType < 11 && (pos ?
                        pos.index(cur) > -1 :
    
                        // Don't pass non-elements to Sizzle
                        cur.nodeType === 1 &&
                            jQuery.find.matchesSelector(cur, selectors)) ) {
    
                        matched.push( cur );
                        break;
                    }
                }
            }
    
            return this.pushStack( matched.length > 1 ? jQuery.unique( matched ) : matched );
        },
    
        // Determine the position of an element within
        // the matched set of elements
        index: function( elem ) {
    
            // No argument, return index in parent
            if ( !elem ) {
                return ( this[ 0 ] && this[ 0 ].parentNode ) ? this.first().prevAll().length : -1;
            }
    
            // index in selector
            if ( typeof elem === "string" ) {
                return indexOf.call( jQuery( elem ), this[ 0 ] );
            }
    
            // Locate the position of the desired element
            return indexOf.call( this,
    
                // If it receives a jQuery object, the first element is used
                elem.jquery ? elem[ 0 ] : elem
            );
        },
    
        add: function( selector, context ) {
            return this.pushStack(
                jQuery.unique(
                    jQuery.merge( this.get(), jQuery( selector, context ) )
                )
            );
        },
    
        addBack: function( selector ) {
            return this.add( selector == null ?
                this.prevObject : this.prevObject.filter(selector)
            );
        }
    });
    
    function sibling( cur, dir ) {
        while ( (cur = cur[dir]) && cur.nodeType !== 1 ) {}
        return cur;
    }
    
    jQuery.each({
        parent: function( elem ) {
            var parent = elem.parentNode;
            return parent && parent.nodeType !== 11 ? parent : null;
        },
        parents: function( elem ) {
            return jQuery.dir( elem, "parentNode" );
        },
        parentsUntil: function( elem, i, until ) {
            return jQuery.dir( elem, "parentNode", until );
        },
        next: function( elem ) {
            return sibling( elem, "nextSibling" );
        },
        prev: function( elem ) {
            return sibling( elem, "previousSibling" );
        },
        nextAll: function( elem ) {
            return jQuery.dir( elem, "nextSibling" );
        },
        prevAll: function( elem ) {
            return jQuery.dir( elem, "previousSibling" );
        },
        nextUntil: function( elem, i, until ) {
            return jQuery.dir( elem, "nextSibling", until );
        },
        prevUntil: function( elem, i, until ) {
            return jQuery.dir( elem, "previousSibling", until );
        },
        siblings: function( elem ) {
            return jQuery.sibling( ( elem.parentNode || {} ).firstChild, elem );
        },
        children: function( elem ) {
            return jQuery.sibling( elem.firstChild );
        },
        contents: function( elem ) {
            return elem.contentDocument || jQuery.merge( [], elem.childNodes );
        }
    }, function( name, fn ) {
        jQuery.fn[ name ] = function( until, selector ) {
            var matched = jQuery.map( this, fn, until );
    
            if ( name.slice( -5 ) !== "Until" ) {
                selector = until;
            }
    
            if ( selector && typeof selector === "string" ) {
                matched = jQuery.filter( selector, matched );
            }
    
            if ( this.length > 1 ) {
                // Remove duplicates
                if ( !guaranteedUnique[ name ] ) {
                    jQuery.unique( matched );
                }
    
                // Reverse order for parents* and prev-derivatives
                if ( rparentsprev.test( name ) ) {
                    matched.reverse();
                }
            }
    
            return this.pushStack( matched );
        };
    });
    var rnotwhite = (/\S+/g);
    
    
    
    // String to Object options format cache
    var optionsCache = {};
    
    // Convert String-formatted options into Object-formatted ones and store in cache
    function createOptions( options ) {
        var object = optionsCache[ options ] = {};
        jQuery.each( options.match( rnotwhite ) || [], function( _, flag ) {
            object[ flag ] = true;
        });
        return object;
    }
    
    /*
     * Create a callback list using the following parameters:
     *
     *	options: an optional list of space-separated options that will change how
     *			the callback list behaves or a more traditional option object
     *
     * By default a callback list will act like an event callback list and can be
     * "fired" multiple times.
     *
     * Possible options:
     *
     *	once:			will ensure the callback list can only be fired once (like a Deferred)
     *
     *	memory:			will keep track of previous values and will call any callback added
     *					after the list has been fired right away with the latest "memorized"
     *					values (like a Deferred)
     *
     *	unique:			will ensure a callback can only be added once (no duplicate in the list)
     *
     *	stopOnFalse:	interrupt callings when a callback returns false
     *
     */
    jQuery.Callbacks = function( options ) {
    
        // Convert options from String-formatted to Object-formatted if needed
        // (we check in cache first)
        options = typeof options === "string" ?
            ( optionsCache[ options ] || createOptions( options ) ) :
            jQuery.extend( {}, options );
    
        var // Last fire value (for non-forgettable lists)
            memory,
            // Flag to know if list was already fired
            fired,
            // Flag to know if list is currently firing
            firing,
            // First callback to fire (used internally by add and fireWith)
            firingStart,
            // End of the loop when firing
            firingLength,
            // Index of currently firing callback (modified by remove if needed)
            firingIndex,
            // Actual callback list
            list = [],
            // Stack of fire calls for repeatable lists
            stack = !options.once && [],
            // Fire callbacks
            fire = function( data ) {
                memory = options.memory && data;
                fired = true;
                firingIndex = firingStart || 0;
                firingStart = 0;
                firingLength = list.length;
                firing = true;
                for ( ; list && firingIndex < firingLength; firingIndex++ ) {
                    if ( list[ firingIndex ].apply( data[ 0 ], data[ 1 ] ) === false && options.stopOnFalse ) {
                        memory = false; // To prevent further calls using add
                        break;
                    }
                }
                firing = false;
                if ( list ) {
                    if ( stack ) {
                        if ( stack.length ) {
                            fire( stack.shift() );
                        }
                    } else if ( memory ) {
                        list = [];
                    } else {
                        self.disable();
                    }
                }
            },
            // Actual Callbacks object
            self = {
                // Add a callback or a collection of callbacks to the list
                add: function() {
                    if ( list ) {
                        // First, we save the current length
                        var start = list.length;
                        (function add( args ) {
                            jQuery.each( args, function( _, arg ) {
                                var type = jQuery.type( arg );
                                if ( type === "function" ) {
                                    if ( !options.unique || !self.has( arg ) ) {
                                        list.push( arg );
                                    }
                                } else if ( arg && arg.length && type !== "string" ) {
                                    // Inspect recursively
                                    add( arg );
                                }
                            });
                        })( arguments );
                        // Do we need to add the callbacks to the
                        // current firing batch?
                        if ( firing ) {
                            firingLength = list.length;
                        // With memory, if we're not firing then
                        // we should call right away
                        } else if ( memory ) {
                            firingStart = start;
                            fire( memory );
                        }
                    }
                    return this;
                },
                // Remove a callback from the list
                remove: function() {
                    if ( list ) {
                        jQuery.each( arguments, function( _, arg ) {
                            var index;
                            while ( ( index = jQuery.inArray( arg, list, index ) ) > -1 ) {
                                list.splice( index, 1 );
                                // Handle firing indexes
                                if ( firing ) {
                                    if ( index <= firingLength ) {
                                        firingLength--;
                                    }
                                    if ( index <= firingIndex ) {
                                        firingIndex--;
                                    }
                                }
                            }
                        });
                    }
                    return this;
                },
                // Check if a given callback is in the list.
                // If no argument is given, return whether or not list has callbacks attached.
                has: function( fn ) {
                    return fn ? jQuery.inArray( fn, list ) > -1 : !!( list && list.length );
                },
                // Remove all callbacks from the list
                empty: function() {
                    list = [];
                    firingLength = 0;
                    return this;
                },
                // Have the list do nothing anymore
                disable: function() {
                    list = stack = memory = undefined;
                    return this;
                },
                // Is it disabled?
                disabled: function() {
                    return !list;
                },
                // Lock the list in its current state
                lock: function() {
                    stack = undefined;
                    if ( !memory ) {
                        self.disable();
                    }
                    return this;
                },
                // Is it locked?
                locked: function() {
                    return !stack;
                },
                // Call all callbacks with the given context and arguments
                fireWith: function( context, args ) {
                    if ( list && ( !fired || stack ) ) {
                        args = args || [];
                        args = [ context, args.slice ? args.slice() : args ];
                        if ( firing ) {
                            stack.push( args );
                        } else {
                            fire( args );
                        }
                    }
                    return this;
                },
                // Call all the callbacks with the given arguments
                fire: function() {
                    self.fireWith( this, arguments );
                    return this;
                },
                // To know if the callbacks have already been called at least once
                fired: function() {
                    return !!fired;
                }
            };
    
        return self;
    };
    
    
    jQuery.extend({
    
        Deferred: function( func ) {
            var tuples = [
                    // action, add listener, listener list, final state
                    [ "resolve", "done", jQuery.Callbacks("once memory"), "resolved" ],
                    [ "reject", "fail", jQuery.Callbacks("once memory"), "rejected" ],
                    [ "notify", "progress", jQuery.Callbacks("memory") ]
                ],
                state = "pending",
                promise = {
                    state: function() {
                        return state;
                    },
                    always: function() {
                        deferred.done( arguments ).fail( arguments );
                        return this;
                    },
                    then: function( /* fnDone, fnFail, fnProgress */ ) {
                        var fns = arguments;
                        return jQuery.Deferred(function( newDefer ) {
                            jQuery.each( tuples, function( i, tuple ) {
                                var fn = jQuery.isFunction( fns[ i ] ) && fns[ i ];
                                // deferred[ done | fail | progress ] for forwarding actions to newDefer
                                deferred[ tuple[1] ](function() {
                                    var returned = fn && fn.apply( this, arguments );
                                    if ( returned && jQuery.isFunction( returned.promise ) ) {
                                        returned.promise()
                                            .done( newDefer.resolve )
                                            .fail( newDefer.reject )
                                            .progress( newDefer.notify );
                                    } else {
                                        newDefer[ tuple[ 0 ] + "With" ]( this === promise ? newDefer.promise() : this, fn ? [ returned ] : arguments );
                                    }
                                });
                            });
                            fns = null;
                        }).promise();
                    },
                    // Get a promise for this deferred
                    // If obj is provided, the promise aspect is added to the object
                    promise: function( obj ) {
                        return obj != null ? jQuery.extend( obj, promise ) : promise;
                    }
                },
                deferred = {};
    
            // Keep pipe for back-compat
            promise.pipe = promise.then;
    
            // Add list-specific methods
            jQuery.each( tuples, function( i, tuple ) {
                var list = tuple[ 2 ],
                    stateString = tuple[ 3 ];
    
                // promise[ done | fail | progress ] = list.add
                promise[ tuple[1] ] = list.add;
    
                // Handle state
                if ( stateString ) {
                    list.add(function() {
                        // state = [ resolved | rejected ]
                        state = stateString;
    
                    // [ reject_list | resolve_list ].disable; progress_list.lock
                    }, tuples[ i ^ 1 ][ 2 ].disable, tuples[ 2 ][ 2 ].lock );
                }
    
                // deferred[ resolve | reject | notify ]
                deferred[ tuple[0] ] = function() {
                    deferred[ tuple[0] + "With" ]( this === deferred ? promise : this, arguments );
                    return this;
                };
                deferred[ tuple[0] + "With" ] = list.fireWith;
            });
    
            // Make the deferred a promise
            promise.promise( deferred );
    
            // Call given func if any
            if ( func ) {
                func.call( deferred, deferred );
            }
    
            // All done!
            return deferred;
        },
    
        // Deferred helper
        when: function( subordinate /* , ..., subordinateN */ ) {
            var i = 0,
                resolveValues = slice.call( arguments ),
                length = resolveValues.length,
    
                // the count of uncompleted subordinates
                remaining = length !== 1 || ( subordinate && jQuery.isFunction( subordinate.promise ) ) ? length : 0,
    
                // the master Deferred. If resolveValues consist of only a single Deferred, just use that.
                deferred = remaining === 1 ? subordinate : jQuery.Deferred(),
    
                // Update function for both resolve and progress values
                updateFunc = function( i, contexts, values ) {
                    return function( value ) {
                        contexts[ i ] = this;
                        values[ i ] = arguments.length > 1 ? slice.call( arguments ) : value;
                        if ( values === progressValues ) {
                            deferred.notifyWith( contexts, values );
                        } else if ( !( --remaining ) ) {
                            deferred.resolveWith( contexts, values );
                        }
                    };
                },
    
                progressValues, progressContexts, resolveContexts;
    
            // add listeners to Deferred subordinates; treat others as resolved
            if ( length > 1 ) {
                progressValues = new Array( length );
                progressContexts = new Array( length );
                resolveContexts = new Array( length );
                for ( ; i < length; i++ ) {
                    if ( resolveValues[ i ] && jQuery.isFunction( resolveValues[ i ].promise ) ) {
                        resolveValues[ i ].promise()
                            .done( updateFunc( i, resolveContexts, resolveValues ) )
                            .fail( deferred.reject )
                            .progress( updateFunc( i, progressContexts, progressValues ) );
                    } else {
                        --remaining;
                    }
                }
            }
    
            // if we're not waiting on anything, resolve the master
            if ( !remaining ) {
                deferred.resolveWith( resolveContexts, resolveValues );
            }
    
            return deferred.promise();
        }
    });
    
    
    // The deferred used on DOM ready
    var readyList;
    
    jQuery.fn.ready = function( fn ) {
        // Add the callback
        jQuery.ready.promise().done( fn );
    
        return this;
    };
    
    jQuery.extend({
        // Is the DOM ready to be used? Set to true once it occurs.
        isReady: false,
    
        // A counter to track how many items to wait for before
        // the ready event fires. See #6781
        readyWait: 1,
    
        // Hold (or release) the ready event
        holdReady: function( hold ) {
            if ( hold ) {
                jQuery.readyWait++;
            } else {
                jQuery.ready( true );
            }
        },
    
        // Handle when the DOM is ready
        ready: function( wait ) {
    
            // Abort if there are pending holds or we're already ready
            if ( wait === true ? --jQuery.readyWait : jQuery.isReady ) {
                return;
            }
    
            // Remember that the DOM is ready
            jQuery.isReady = true;
    
            // If a normal DOM Ready event fired, decrement, and wait if need be
            if ( wait !== true && --jQuery.readyWait > 0 ) {
                return;
            }
    
            // If there are functions bound, to execute
            readyList.resolveWith( document, [ jQuery ] );
    
            // Trigger any bound ready events
            if ( jQuery.fn.triggerHandler ) {
                jQuery( document ).triggerHandler( "ready" );
                jQuery( document ).off( "ready" );
            }
        }
    });
    
    /**
     * The ready event handler and self cleanup method
     */
    function completed() {
        document.removeEventListener( "DOMContentLoaded", completed, false );
        window.removeEventListener( "load", completed, false );
        jQuery.ready();
    }
    
    jQuery.ready.promise = function( obj ) {
        if ( !readyList ) {
    
            readyList = jQuery.Deferred();
    
            // Catch cases where $(document).ready() is called after the browser event has already occurred.
            // we once tried to use readyState "interactive" here, but it caused issues like the one
            // discovered by ChrisS here: http://bugs.jquery.com/ticket/12282#comment:15
            if ( document.readyState === "complete" ) {
                // Handle it asynchronously to allow scripts the opportunity to delay ready
                setTimeout( jQuery.ready );
    
            } else {
    
                // Use the handy event callback
                document.addEventListener( "DOMContentLoaded", completed, false );
    
                // A fallback to window.onload, that will always work
                window.addEventListener( "load", completed, false );
            }
        }
        return readyList.promise( obj );
    };
    
    // Kick off the DOM ready check even if the user does not
    jQuery.ready.promise();
    
    
    
    
    // Multifunctional method to get and set values of a collection
    // The value/s can optionally be executed if it's a function
    var access = jQuery.access = function( elems, fn, key, value, chainable, emptyGet, raw ) {
        var i = 0,
            len = elems.length,
            bulk = key == null;
    
        // Sets many values
        if ( jQuery.type( key ) === "object" ) {
            chainable = true;
            for ( i in key ) {
                jQuery.access( elems, fn, i, key[i], true, emptyGet, raw );
            }
    
        // Sets one value
        } else if ( value !== undefined ) {
            chainable = true;
    
            if ( !jQuery.isFunction( value ) ) {
                raw = true;
            }
    
            if ( bulk ) {
                // Bulk operations run against the entire set
                if ( raw ) {
                    fn.call( elems, value );
                    fn = null;
    
                // ...except when executing function values
                } else {
                    bulk = fn;
                    fn = function( elem, key, value ) {
                        return bulk.call( jQuery( elem ), value );
                    };
                }
            }
    
            if ( fn ) {
                for ( ; i < len; i++ ) {
                    fn( elems[i], key, raw ? value : value.call( elems[i], i, fn( elems[i], key ) ) );
                }
            }
        }
    
        return chainable ?
            elems :
    
            // Gets
            bulk ?
                fn.call( elems ) :
                len ? fn( elems[0], key ) : emptyGet;
    };
    
    
    /**
     * Determines whether an object can have data
     */
    jQuery.acceptData = function( owner ) {
        // Accepts only:
        //  - Node
        //    - Node.ELEMENT_NODE
        //    - Node.DOCUMENT_NODE
        //  - Object
        //    - Any
        /* jshint -W018 */
        return owner.nodeType === 1 || owner.nodeType === 9 || !( +owner.nodeType );
    };
    
    
    function Data() {
        // Support: Android < 4,
        // Old WebKit does not have Object.preventExtensions/freeze method,
        // return new empty object instead with no [[set]] accessor
        Object.defineProperty( this.cache = {}, 0, {
            get: function() {
                return {};
            }
        });
    
        this.expando = jQuery.expando + Math.random();
    }
    
    Data.uid = 1;
    Data.accepts = jQuery.acceptData;
    
    Data.prototype = {
        key: function( owner ) {
            // We can accept data for non-element nodes in modern browsers,
            // but we should not, see #8335.
            // Always return the key for a frozen object.
            if ( !Data.accepts( owner ) ) {
                return 0;
            }
    
            var descriptor = {},
                // Check if the owner object already has a cache key
                unlock = owner[ this.expando ];
    
            // If not, create one
            if ( !unlock ) {
                unlock = Data.uid++;
    
                // Secure it in a non-enumerable, non-writable property
                try {
                    descriptor[ this.expando ] = { value: unlock };
                    Object.defineProperties( owner, descriptor );
    
                // Support: Android < 4
                // Fallback to a less secure definition
                } catch ( e ) {
                    descriptor[ this.expando ] = unlock;
                    jQuery.extend( owner, descriptor );
                }
            }
    
            // Ensure the cache object
            if ( !this.cache[ unlock ] ) {
                this.cache[ unlock ] = {};
            }
    
            return unlock;
        },
        set: function( owner, data, value ) {
            var prop,
                // There may be an unlock assigned to this node,
                // if there is no entry for this "owner", create one inline
                // and set the unlock as though an owner entry had always existed
                unlock = this.key( owner ),
                cache = this.cache[ unlock ];
    
            // Handle: [ owner, key, value ] args
            if ( typeof data === "string" ) {
                cache[ data ] = value;
    
            // Handle: [ owner, { properties } ] args
            } else {
                // Fresh assignments by object are shallow copied
                if ( jQuery.isEmptyObject( cache ) ) {
                    jQuery.extend( this.cache[ unlock ], data );
                // Otherwise, copy the properties one-by-one to the cache object
                } else {
                    for ( prop in data ) {
                        cache[ prop ] = data[ prop ];
                    }
                }
            }
            return cache;
        },
        get: function( owner, key ) {
            // Either a valid cache is found, or will be created.
            // New caches will be created and the unlock returned,
            // allowing direct access to the newly created
            // empty data object. A valid owner object must be provided.
            var cache = this.cache[ this.key( owner ) ];
    
            return key === undefined ?
                cache : cache[ key ];
        },
        access: function( owner, key, value ) {
            var stored;
            // In cases where either:
            //
            //   1. No key was specified
            //   2. A string key was specified, but no value provided
            //
            // Take the "read" path and allow the get method to determine
            // which value to return, respectively either:
            //
            //   1. The entire cache object
            //   2. The data stored at the key
            //
            if ( key === undefined ||
                    ((key && typeof key === "string") && value === undefined) ) {
    
                stored = this.get( owner, key );
    
                return stored !== undefined ?
                    stored : this.get( owner, jQuery.camelCase(key) );
            }
    
            // [*]When the key is not a string, or both a key and value
            // are specified, set or extend (existing objects) with either:
            //
            //   1. An object of properties
            //   2. A key and value
            //
            this.set( owner, key, value );
    
            // Since the "set" path can have two possible entry points
            // return the expected data based on which path was taken[*]
            return value !== undefined ? value : key;
        },
        remove: function( owner, key ) {
            var i, name, camel,
                unlock = this.key( owner ),
                cache = this.cache[ unlock ];
    
            if ( key === undefined ) {
                this.cache[ unlock ] = {};
    
            } else {
                // Support array or space separated string of keys
                if ( jQuery.isArray( key ) ) {
                    // If "name" is an array of keys...
                    // When data is initially created, via ("key", "val") signature,
                    // keys will be converted to camelCase.
                    // Since there is no way to tell _how_ a key was added, remove
                    // both plain key and camelCase key. #12786
                    // This will only penalize the array argument path.
                    name = key.concat( key.map( jQuery.camelCase ) );
                } else {
                    camel = jQuery.camelCase( key );
                    // Try the string as a key before any manipulation
                    if ( key in cache ) {
                        name = [ key, camel ];
                    } else {
                        // If a key with the spaces exists, use it.
                        // Otherwise, create an array by matching non-whitespace
                        name = camel;
                        name = name in cache ?
                            [ name ] : ( name.match( rnotwhite ) || [] );
                    }
                }
    
                i = name.length;
                while ( i-- ) {
                    delete cache[ name[ i ] ];
                }
            }
        },
        hasData: function( owner ) {
            return !jQuery.isEmptyObject(
                this.cache[ owner[ this.expando ] ] || {}
            );
        },
        discard: function( owner ) {
            if ( owner[ this.expando ] ) {
                delete this.cache[ owner[ this.expando ] ];
            }
        }
    };
    var data_priv = new Data();
    
    var data_user = new Data();
    
    
    
    /*
        Implementation Summary
    
        1. Enforce API surface and semantic compatibility with 1.9.x branch
        2. Improve the module's maintainability by reducing the storage
            paths to a single mechanism.
        3. Use the same single mechanism to support "private" and "user" data.
        4. _Never_ expose "private" data to user code (TODO: Drop _data, _removeData)
        5. Avoid exposing implementation details on user objects (eg. expando properties)
        6. Provide a clear path for implementation upgrade to WeakMap in 2014
    */
    var rbrace = /^(?:\{[\w\W]*\}|\[[\w\W]*\])$/,
        rmultiDash = /([A-Z])/g;
    
    function dataAttr( elem, key, data ) {
        var name;
    
        // If nothing was found internally, try to fetch any
        // data from the HTML5 data-* attribute
        if ( data === undefined && elem.nodeType === 1 ) {
            name = "data-" + key.replace( rmultiDash, "-$1" ).toLowerCase();
            data = elem.getAttribute( name );
    
            if ( typeof data === "string" ) {
                try {
                    data = data === "true" ? true :
                        data === "false" ? false :
                        data === "null" ? null :
                        // Only convert to a number if it doesn't change the string
                        +data + "" === data ? +data :
                        rbrace.test( data ) ? jQuery.parseJSON( data ) :
                        data;
                } catch( e ) {}
    
                // Make sure we set the data so it isn't changed later
                data_user.set( elem, key, data );
            } else {
                data = undefined;
            }
        }
        return data;
    }
    
    jQuery.extend({
        hasData: function( elem ) {
            return data_user.hasData( elem ) || data_priv.hasData( elem );
        },
    
        data: function( elem, name, data ) {
            return data_user.access( elem, name, data );
        },
    
        removeData: function( elem, name ) {
            data_user.remove( elem, name );
        },
    
        // TODO: Now that all calls to _data and _removeData have been replaced
        // with direct calls to data_priv methods, these can be deprecated.
        _data: function( elem, name, data ) {
            return data_priv.access( elem, name, data );
        },
    
        _removeData: function( elem, name ) {
            data_priv.remove( elem, name );
        }
    });
    
    jQuery.fn.extend({
        data: function( key, value ) {
            var i, name, data,
                elem = this[ 0 ],
                attrs = elem && elem.attributes;
    
            // Gets all values
            if ( key === undefined ) {
                if ( this.length ) {
                    data = data_user.get( elem );
    
                    if ( elem.nodeType === 1 && !data_priv.get( elem, "hasDataAttrs" ) ) {
                        i = attrs.length;
                        while ( i-- ) {
    
                            // Support: IE11+
                            // The attrs elements can be null (#14894)
                            if ( attrs[ i ] ) {
                                name = attrs[ i ].name;
                                if ( name.indexOf( "data-" ) === 0 ) {
                                    name = jQuery.camelCase( name.slice(5) );
                                    dataAttr( elem, name, data[ name ] );
                                }
                            }
                        }
                        data_priv.set( elem, "hasDataAttrs", true );
                    }
                }
    
                return data;
            }
    
            // Sets multiple values
            if ( typeof key === "object" ) {
                return this.each(function() {
                    data_user.set( this, key );
                });
            }
    
            return access( this, function( value ) {
                var data,
                    camelKey = jQuery.camelCase( key );
    
                // The calling jQuery object (element matches) is not empty
                // (and therefore has an element appears at this[ 0 ]) and the
                // `value` parameter was not undefined. An empty jQuery object
                // will result in `undefined` for elem = this[ 0 ] which will
                // throw an exception if an attempt to read a data cache is made.
                if ( elem && value === undefined ) {
                    // Attempt to get data from the cache
                    // with the key as-is
                    data = data_user.get( elem, key );
                    if ( data !== undefined ) {
                        return data;
                    }
    
                    // Attempt to get data from the cache
                    // with the key camelized
                    data = data_user.get( elem, camelKey );
                    if ( data !== undefined ) {
                        return data;
                    }
    
                    // Attempt to "discover" the data in
                    // HTML5 custom data-* attrs
                    data = dataAttr( elem, camelKey, undefined );
                    if ( data !== undefined ) {
                        return data;
                    }
    
                    // We tried really hard, but the data doesn't exist.
                    return;
                }
    
                // Set the data...
                this.each(function() {
                    // First, attempt to store a copy or reference of any
                    // data that might've been store with a camelCased key.
                    var data = data_user.get( this, camelKey );
    
                    // For HTML5 data-* attribute interop, we have to
                    // store property names with dashes in a camelCase form.
                    // This might not apply to all properties...*
                    data_user.set( this, camelKey, value );
    
                    // *... In the case of properties that might _actually_
                    // have dashes, we need to also store a copy of that
                    // unchanged property.
                    if ( key.indexOf("-") !== -1 && data !== undefined ) {
                        data_user.set( this, key, value );
                    }
                });
            }, null, value, arguments.length > 1, null, true );
        },
    
        removeData: function( key ) {
            return this.each(function() {
                data_user.remove( this, key );
            });
        }
    });
    
    
    jQuery.extend({
        queue: function( elem, type, data ) {
            var queue;
    
            if ( elem ) {
                type = ( type || "fx" ) + "queue";
                queue = data_priv.get( elem, type );
    
                // Speed up dequeue by getting out quickly if this is just a lookup
                if ( data ) {
                    if ( !queue || jQuery.isArray( data ) ) {
                        queue = data_priv.access( elem, type, jQuery.makeArray(data) );
                    } else {
                        queue.push( data );
                    }
                }
                return queue || [];
            }
        },
    
        dequeue: function( elem, type ) {
            type = type || "fx";
    
            var queue = jQuery.queue( elem, type ),
                startLength = queue.length,
                fn = queue.shift(),
                hooks = jQuery._queueHooks( elem, type ),
                next = function() {
                    jQuery.dequeue( elem, type );
                };
    
            // If the fx queue is dequeued, always remove the progress sentinel
            if ( fn === "inprogress" ) {
                fn = queue.shift();
                startLength--;
            }
    
            if ( fn ) {
    
                // Add a progress sentinel to prevent the fx queue from being
                // automatically dequeued
                if ( type === "fx" ) {
                    queue.unshift( "inprogress" );
                }
    
                // clear up the last queue stop function
                delete hooks.stop;
                fn.call( elem, next, hooks );
            }
    
            if ( !startLength && hooks ) {
                hooks.empty.fire();
            }
        },
    
        // not intended for public consumption - generates a queueHooks object, or returns the current one
        _queueHooks: function( elem, type ) {
            var key = type + "queueHooks";
            return data_priv.get( elem, key ) || data_priv.access( elem, key, {
                empty: jQuery.Callbacks("once memory").add(function() {
                    data_priv.remove( elem, [ type + "queue", key ] );
                })
            });
        }
    });
    
    jQuery.fn.extend({
        queue: function( type, data ) {
            var setter = 2;
    
            if ( typeof type !== "string" ) {
                data = type;
                type = "fx";
                setter--;
            }
    
            if ( arguments.length < setter ) {
                return jQuery.queue( this[0], type );
            }
    
            return data === undefined ?
                this :
                this.each(function() {
                    var queue = jQuery.queue( this, type, data );
    
                    // ensure a hooks for this queue
                    jQuery._queueHooks( this, type );
    
                    if ( type === "fx" && queue[0] !== "inprogress" ) {
                        jQuery.dequeue( this, type );
                    }
                });
        },
        dequeue: function( type ) {
            return this.each(function() {
                jQuery.dequeue( this, type );
            });
        },
        clearQueue: function( type ) {
            return this.queue( type || "fx", [] );
        },
        // Get a promise resolved when queues of a certain type
        // are emptied (fx is the type by default)
        promise: function( type, obj ) {
            var tmp,
                count = 1,
                defer = jQuery.Deferred(),
                elements = this,
                i = this.length,
                resolve = function() {
                    if ( !( --count ) ) {
                        defer.resolveWith( elements, [ elements ] );
                    }
                };
    
            if ( typeof type !== "string" ) {
                obj = type;
                type = undefined;
            }
            type = type || "fx";
    
            while ( i-- ) {
                tmp = data_priv.get( elements[ i ], type + "queueHooks" );
                if ( tmp && tmp.empty ) {
                    count++;
                    tmp.empty.add( resolve );
                }
            }
            resolve();
            return defer.promise( obj );
        }
    });
    var pnum = (/[+-]?(?:\d*\.|)\d+(?:[eE][+-]?\d+|)/).source;
    
    var cssExpand = [ "Top", "Right", "Bottom", "Left" ];
    
    var isHidden = function( elem, el ) {
            // isHidden might be called from jQuery#filter function;
            // in that case, element will be second argument
            elem = el || elem;
            return jQuery.css( elem, "display" ) === "none" || !jQuery.contains( elem.ownerDocument, elem );
        };
    
    var rcheckableType = (/^(?:checkbox|radio)$/i);
    
    
    
    (function() {
        var fragment = document.createDocumentFragment(),
            div = fragment.appendChild( document.createElement( "div" ) ),
            input = document.createElement( "input" );
    
        // #11217 - WebKit loses check when the name is after the checked attribute
        // Support: Windows Web Apps (WWA)
        // `name` and `type` need .setAttribute for WWA
        input.setAttribute( "type", "radio" );
        input.setAttribute( "checked", "checked" );
        input.setAttribute( "name", "t" );
    
        div.appendChild( input );
    
        // Support: Safari 5.1, iOS 5.1, Android 4.x, Android 2.3
        // old WebKit doesn't clone checked state correctly in fragments
        support.checkClone = div.cloneNode( true ).cloneNode( true ).lastChild.checked;
    
        // Make sure textarea (and checkbox) defaultValue is properly cloned
        // Support: IE9-IE11+
        div.innerHTML = "<textarea>x</textarea>";
        support.noCloneChecked = !!div.cloneNode( true ).lastChild.defaultValue;
    })();
    var strundefined = typeof undefined;
    
    
    
    support.focusinBubbles = "onfocusin" in window;
    
    
    var
        rkeyEvent = /^key/,
        rmouseEvent = /^(?:mouse|pointer|contextmenu)|click/,
        rfocusMorph = /^(?:focusinfocus|focusoutblur)$/,
        rtypenamespace = /^([^.]*)(?:\.(.+)|)$/;
    
    function returnTrue() {
        return true;
    }
    
    function returnFalse() {
        return false;
    }
    
    function safeActiveElement() {
        try {
            return document.activeElement;
        } catch ( err ) { }
    }
    
    /*
     * Helper functions for managing events -- not part of the public interface.
     * Props to Dean Edwards' addEvent library for many of the ideas.
     */
    jQuery.event = {
    
        global: {},
    
        add: function( elem, types, handler, data, selector ) {
    
            var handleObjIn, eventHandle, tmp,
                events, t, handleObj,
                special, handlers, type, namespaces, origType,
                elemData = data_priv.get( elem );
    
            // Don't attach events to noData or text/comment nodes (but allow plain objects)
            if ( !elemData ) {
                return;
            }
    
            // Caller can pass in an object of custom data in lieu of the handler
            if ( handler.handler ) {
                handleObjIn = handler;
                handler = handleObjIn.handler;
                selector = handleObjIn.selector;
            }
    
            // Make sure that the handler has a unique ID, used to find/remove it later
            if ( !handler.guid ) {
                handler.guid = jQuery.guid++;
            }
    
            // Init the element's event structure and main handler, if this is the first
            if ( !(events = elemData.events) ) {
                events = elemData.events = {};
            }
            if ( !(eventHandle = elemData.handle) ) {
                eventHandle = elemData.handle = function( e ) {
                    // Discard the second event of a jQuery.event.trigger() and
                    // when an event is called after a page has unloaded
                    return typeof jQuery !== strundefined && jQuery.event.triggered !== e.type ?
                        jQuery.event.dispatch.apply( elem, arguments ) : undefined;
                };
            }
    
            // Handle multiple events separated by a space
            types = ( types || "" ).match( rnotwhite ) || [ "" ];
            t = types.length;
            while ( t-- ) {
                tmp = rtypenamespace.exec( types[t] ) || [];
                type = origType = tmp[1];
                namespaces = ( tmp[2] || "" ).split( "." ).sort();
    
                // There *must* be a type, no attaching namespace-only handlers
                if ( !type ) {
                    continue;
                }
    
                // If event changes its type, use the special event handlers for the changed type
                special = jQuery.event.special[ type ] || {};
    
                // If selector defined, determine special event api type, otherwise given type
                type = ( selector ? special.delegateType : special.bindType ) || type;
    
                // Update special based on newly reset type
                special = jQuery.event.special[ type ] || {};
    
                // handleObj is passed to all event handlers
                handleObj = jQuery.extend({
                    type: type,
                    origType: origType,
                    data: data,
                    handler: handler,
                    guid: handler.guid,
                    selector: selector,
                    needsContext: selector && jQuery.expr.match.needsContext.test( selector ),
                    namespace: namespaces.join(".")
                }, handleObjIn );
    
                // Init the event handler queue if we're the first
                if ( !(handlers = events[ type ]) ) {
                    handlers = events[ type ] = [];
                    handlers.delegateCount = 0;
    
                    // Only use addEventListener if the special events handler returns false
                    if ( !special.setup || special.setup.call( elem, data, namespaces, eventHandle ) === false ) {
                        if ( elem.addEventListener ) {
                            elem.addEventListener( type, eventHandle, false );
                        }
                    }
                }
    
                if ( special.add ) {
                    special.add.call( elem, handleObj );
    
                    if ( !handleObj.handler.guid ) {
                        handleObj.handler.guid = handler.guid;
                    }
                }
    
                // Add to the element's handler list, delegates in front
                if ( selector ) {
                    handlers.splice( handlers.delegateCount++, 0, handleObj );
                } else {
                    handlers.push( handleObj );
                }
    
                // Keep track of which events have ever been used, for event optimization
                jQuery.event.global[ type ] = true;
            }
    
        },
    
        // Detach an event or set of events from an element
        remove: function( elem, types, handler, selector, mappedTypes ) {
    
            var j, origCount, tmp,
                events, t, handleObj,
                special, handlers, type, namespaces, origType,
                elemData = data_priv.hasData( elem ) && data_priv.get( elem );
    
            if ( !elemData || !(events = elemData.events) ) {
                return;
            }
    
            // Once for each type.namespace in types; type may be omitted
            types = ( types || "" ).match( rnotwhite ) || [ "" ];
            t = types.length;
            while ( t-- ) {
                tmp = rtypenamespace.exec( types[t] ) || [];
                type = origType = tmp[1];
                namespaces = ( tmp[2] || "" ).split( "." ).sort();
    
                // Unbind all events (on this namespace, if provided) for the element
                if ( !type ) {
                    for ( type in events ) {
                        jQuery.event.remove( elem, type + types[ t ], handler, selector, true );
                    }
                    continue;
                }
    
                special = jQuery.event.special[ type ] || {};
                type = ( selector ? special.delegateType : special.bindType ) || type;
                handlers = events[ type ] || [];
                tmp = tmp[2] && new RegExp( "(^|\\.)" + namespaces.join("\\.(?:.*\\.|)") + "(\\.|$)" );
    
                // Remove matching events
                origCount = j = handlers.length;
                while ( j-- ) {
                    handleObj = handlers[ j ];
    
                    if ( ( mappedTypes || origType === handleObj.origType ) &&
                        ( !handler || handler.guid === handleObj.guid ) &&
                        ( !tmp || tmp.test( handleObj.namespace ) ) &&
                        ( !selector || selector === handleObj.selector || selector === "**" && handleObj.selector ) ) {
                        handlers.splice( j, 1 );
    
                        if ( handleObj.selector ) {
                            handlers.delegateCount--;
                        }
                        if ( special.remove ) {
                            special.remove.call( elem, handleObj );
                        }
                    }
                }
    
                // Remove generic event handler if we removed something and no more handlers exist
                // (avoids potential for endless recursion during removal of special event handlers)
                if ( origCount && !handlers.length ) {
                    if ( !special.teardown || special.teardown.call( elem, namespaces, elemData.handle ) === false ) {
                        jQuery.removeEvent( elem, type, elemData.handle );
                    }
    
                    delete events[ type ];
                }
            }
    
            // Remove the expando if it's no longer used
            if ( jQuery.isEmptyObject( events ) ) {
                delete elemData.handle;
                data_priv.remove( elem, "events" );
            }
        },
    
        trigger: function( event, data, elem, onlyHandlers ) {
    
            var i, cur, tmp, bubbleType, ontype, handle, special,
                eventPath = [ elem || document ],
                type = hasOwn.call( event, "type" ) ? event.type : event,
                namespaces = hasOwn.call( event, "namespace" ) ? event.namespace.split(".") : [];
    
            cur = tmp = elem = elem || document;
    
            // Don't do events on text and comment nodes
            if ( elem.nodeType === 3 || elem.nodeType === 8 ) {
                return;
            }
    
            // focus/blur morphs to focusin/out; ensure we're not firing them right now
            if ( rfocusMorph.test( type + jQuery.event.triggered ) ) {
                return;
            }
    
            if ( type.indexOf(".") >= 0 ) {
                // Namespaced trigger; create a regexp to match event type in handle()
                namespaces = type.split(".");
                type = namespaces.shift();
                namespaces.sort();
            }
            ontype = type.indexOf(":") < 0 && "on" + type;
    
            // Caller can pass in a jQuery.Event object, Object, or just an event type string
            event = event[ jQuery.expando ] ?
                event :
                new jQuery.Event( type, typeof event === "object" && event );
    
            // Trigger bitmask: & 1 for native handlers; & 2 for jQuery (always true)
            event.isTrigger = onlyHandlers ? 2 : 3;
            event.namespace = namespaces.join(".");
            event.namespace_re = event.namespace ?
                new RegExp( "(^|\\.)" + namespaces.join("\\.(?:.*\\.|)") + "(\\.|$)" ) :
                null;
    
            // Clean up the event in case it is being reused
            event.result = undefined;
            if ( !event.target ) {
                event.target = elem;
            }
    
            // Clone any incoming data and prepend the event, creating the handler arg list
            data = data == null ?
                [ event ] :
                jQuery.makeArray( data, [ event ] );
    
            // Allow special events to draw outside the lines
            special = jQuery.event.special[ type ] || {};
            if ( !onlyHandlers && special.trigger && special.trigger.apply( elem, data ) === false ) {
                return;
            }
    
            // Determine event propagation path in advance, per W3C events spec (#9951)
            // Bubble up to document, then to window; watch for a global ownerDocument var (#9724)
            if ( !onlyHandlers && !special.noBubble && !jQuery.isWindow( elem ) ) {
    
                bubbleType = special.delegateType || type;
                if ( !rfocusMorph.test( bubbleType + type ) ) {
                    cur = cur.parentNode;
                }
                for ( ; cur; cur = cur.parentNode ) {
                    eventPath.push( cur );
                    tmp = cur;
                }
    
                // Only add window if we got to document (e.g., not plain obj or detached DOM)
                if ( tmp === (elem.ownerDocument || document) ) {
                    eventPath.push( tmp.defaultView || tmp.parentWindow || window );
                }
            }
    
            // Fire handlers on the event path
            i = 0;
            while ( (cur = eventPath[i++]) && !event.isPropagationStopped() ) {
    
                event.type = i > 1 ?
                    bubbleType :
                    special.bindType || type;
    
                // jQuery handler
                handle = ( data_priv.get( cur, "events" ) || {} )[ event.type ] && data_priv.get( cur, "handle" );
                if ( handle ) {
                    handle.apply( cur, data );
                }
    
                // Native handler
                handle = ontype && cur[ ontype ];
                if ( handle && handle.apply && jQuery.acceptData( cur ) ) {
                    event.result = handle.apply( cur, data );
                    if ( event.result === false ) {
                        event.preventDefault();
                    }
                }
            }
            event.type = type;
    
            // If nobody prevented the default action, do it now
            if ( !onlyHandlers && !event.isDefaultPrevented() ) {
    
                if ( (!special._default || special._default.apply( eventPath.pop(), data ) === false) &&
                    jQuery.acceptData( elem ) ) {
    
                    // Call a native DOM method on the target with the same name name as the event.
                    // Don't do default actions on window, that's where global variables be (#6170)
                    if ( ontype && jQuery.isFunction( elem[ type ] ) && !jQuery.isWindow( elem ) ) {
    
                        // Don't re-trigger an onFOO event when we call its FOO() method
                        tmp = elem[ ontype ];
    
                        if ( tmp ) {
                            elem[ ontype ] = null;
                        }
    
                        // Prevent re-triggering of the same event, since we already bubbled it above
                        jQuery.event.triggered = type;
                        elem[ type ]();
                        jQuery.event.triggered = undefined;
    
                        if ( tmp ) {
                            elem[ ontype ] = tmp;
                        }
                    }
                }
            }
    
            return event.result;
        },
    
        dispatch: function( event ) {
    
            // Make a writable jQuery.Event from the native event object
            event = jQuery.event.fix( event );
    
            var i, j, ret, matched, handleObj,
                handlerQueue = [],
                args = slice.call( arguments ),
                handlers = ( data_priv.get( this, "events" ) || {} )[ event.type ] || [],
                special = jQuery.event.special[ event.type ] || {};
    
            // Use the fix-ed jQuery.Event rather than the (read-only) native event
            args[0] = event;
            event.delegateTarget = this;
    
            // Call the preDispatch hook for the mapped type, and let it bail if desired
            if ( special.preDispatch && special.preDispatch.call( this, event ) === false ) {
                return;
            }
    
            // Determine handlers
            handlerQueue = jQuery.event.handlers.call( this, event, handlers );
    
            // Run delegates first; they may want to stop propagation beneath us
            i = 0;
            while ( (matched = handlerQueue[ i++ ]) && !event.isPropagationStopped() ) {
                event.currentTarget = matched.elem;
    
                j = 0;
                while ( (handleObj = matched.handlers[ j++ ]) && !event.isImmediatePropagationStopped() ) {
    
                    // Triggered event must either 1) have no namespace, or
                    // 2) have namespace(s) a subset or equal to those in the bound event (both can have no namespace).
                    if ( !event.namespace_re || event.namespace_re.test( handleObj.namespace ) ) {
    
                        event.handleObj = handleObj;
                        event.data = handleObj.data;
    
                        ret = ( (jQuery.event.special[ handleObj.origType ] || {}).handle || handleObj.handler )
                                .apply( matched.elem, args );
    
                        if ( ret !== undefined ) {
                            if ( (event.result = ret) === false ) {
                                event.preventDefault();
                                event.stopPropagation();
                            }
                        }
                    }
                }
            }
    
            // Call the postDispatch hook for the mapped type
            if ( special.postDispatch ) {
                special.postDispatch.call( this, event );
            }
    
            return event.result;
        },
    
        handlers: function( event, handlers ) {
            var i, matches, sel, handleObj,
                handlerQueue = [],
                delegateCount = handlers.delegateCount,
                cur = event.target;
    
            // Find delegate handlers
            // Black-hole SVG <use> instance trees (#13180)
            // Avoid non-left-click bubbling in Firefox (#3861)
            if ( delegateCount && cur.nodeType && (!event.button || event.type !== "click") ) {
    
                for ( ; cur !== this; cur = cur.parentNode || this ) {
    
                    // Don't process clicks on disabled elements (#6911, #8165, #11382, #11764)
                    if ( cur.disabled !== true || event.type !== "click" ) {
                        matches = [];
                        for ( i = 0; i < delegateCount; i++ ) {
                            handleObj = handlers[ i ];
    
                            // Don't conflict with Object.prototype properties (#13203)
                            sel = handleObj.selector + " ";
    
                            if ( matches[ sel ] === undefined ) {
                                matches[ sel ] = handleObj.needsContext ?
                                    jQuery( sel, this ).index( cur ) >= 0 :
                                    jQuery.find( sel, this, null, [ cur ] ).length;
                            }
                            if ( matches[ sel ] ) {
                                matches.push( handleObj );
                            }
                        }
                        if ( matches.length ) {
                            handlerQueue.push({ elem: cur, handlers: matches });
                        }
                    }
                }
            }
    
            // Add the remaining (directly-bound) handlers
            if ( delegateCount < handlers.length ) {
                handlerQueue.push({ elem: this, handlers: handlers.slice( delegateCount ) });
            }
    
            return handlerQueue;
        },
    
        // Includes some event props shared by KeyEvent and MouseEvent
        props: "altKey bubbles cancelable ctrlKey currentTarget eventPhase metaKey relatedTarget shiftKey target timeStamp view which".split(" "),
    
        fixHooks: {},
    
        keyHooks: {
            props: "char charCode key keyCode".split(" "),
            filter: function( event, original ) {
    
                // Add which for key events
                if ( event.which == null ) {
                    event.which = original.charCode != null ? original.charCode : original.keyCode;
                }
    
                return event;
            }
        },
    
        mouseHooks: {
            props: "button buttons clientX clientY offsetX offsetY pageX pageY screenX screenY toElement".split(" "),
            filter: function( event, original ) {
                var eventDoc, doc, body,
                    button = original.button;
    
                // Calculate pageX/Y if missing and clientX/Y available
                if ( event.pageX == null && original.clientX != null ) {
                    eventDoc = event.target.ownerDocument || document;
                    doc = eventDoc.documentElement;
                    body = eventDoc.body;
    
                    event.pageX = original.clientX + ( doc && doc.scrollLeft || body && body.scrollLeft || 0 ) - ( doc && doc.clientLeft || body && body.clientLeft || 0 );
                    event.pageY = original.clientY + ( doc && doc.scrollTop  || body && body.scrollTop  || 0 ) - ( doc && doc.clientTop  || body && body.clientTop  || 0 );
                }
    
                // Add which for click: 1 === left; 2 === middle; 3 === right
                // Note: button is not normalized, so don't use it
                if ( !event.which && button !== undefined ) {
                    event.which = ( button & 1 ? 1 : ( button & 2 ? 3 : ( button & 4 ? 2 : 0 ) ) );
                }
    
                return event;
            }
        },
    
        fix: function( event ) {
            if ( event[ jQuery.expando ] ) {
                return event;
            }
    
            // Create a writable copy of the event object and normalize some properties
            var i, prop, copy,
                type = event.type,
                originalEvent = event,
                fixHook = this.fixHooks[ type ];
    
            if ( !fixHook ) {
                this.fixHooks[ type ] = fixHook =
                    rmouseEvent.test( type ) ? this.mouseHooks :
                    rkeyEvent.test( type ) ? this.keyHooks :
                    {};
            }
            copy = fixHook.props ? this.props.concat( fixHook.props ) : this.props;
    
            event = new jQuery.Event( originalEvent );
    
            i = copy.length;
            while ( i-- ) {
                prop = copy[ i ];
                event[ prop ] = originalEvent[ prop ];
            }
    
            // Support: Cordova 2.5 (WebKit) (#13255)
            // All events should have a target; Cordova deviceready doesn't
            if ( !event.target ) {
                event.target = document;
            }
    
            // Support: Safari 6.0+, Chrome < 28
            // Target should not be a text node (#504, #13143)
            if ( event.target.nodeType === 3 ) {
                event.target = event.target.parentNode;
            }
    
            return fixHook.filter ? fixHook.filter( event, originalEvent ) : event;
        },
    
        special: {
            load: {
                // Prevent triggered image.load events from bubbling to window.load
                noBubble: true
            },
            focus: {
                // Fire native event if possible so blur/focus sequence is correct
                trigger: function() {
                    if ( this !== safeActiveElement() && this.focus ) {
                        this.focus();
                        return false;
                    }
                },
                delegateType: "focusin"
            },
            blur: {
                trigger: function() {
                    if ( this === safeActiveElement() && this.blur ) {
                        this.blur();
                        return false;
                    }
                },
                delegateType: "focusout"
            },
            click: {
                // For checkbox, fire native event so checked state will be right
                trigger: function() {
                    if ( this.type === "checkbox" && this.click && jQuery.nodeName( this, "input" ) ) {
                        this.click();
                        return false;
                    }
                },
    
                // For cross-browser consistency, don't fire native .click() on links
                _default: function( event ) {
                    return jQuery.nodeName( event.target, "a" );
                }
            },
    
            beforeunload: {
                postDispatch: function( event ) {
    
                    // Support: Firefox 20+
                    // Firefox doesn't alert if the returnValue field is not set.
                    if ( event.result !== undefined && event.originalEvent ) {
                        event.originalEvent.returnValue = event.result;
                    }
                }
            }
        },
    
        simulate: function( type, elem, event, bubble ) {
            // Piggyback on a donor event to simulate a different one.
            // Fake originalEvent to avoid donor's stopPropagation, but if the
            // simulated event prevents default then we do the same on the donor.
            var e = jQuery.extend(
                new jQuery.Event(),
                event,
                {
                    type: type,
                    isSimulated: true,
                    originalEvent: {}
                }
            );
            if ( bubble ) {
                jQuery.event.trigger( e, null, elem );
            } else {
                jQuery.event.dispatch.call( elem, e );
            }
            if ( e.isDefaultPrevented() ) {
                event.preventDefault();
            }
        }
    };
    
    jQuery.removeEvent = function( elem, type, handle ) {
        if ( elem.removeEventListener ) {
            elem.removeEventListener( type, handle, false );
        }
    };
    
    jQuery.Event = function( src, props ) {
        // Allow instantiation without the 'new' keyword
        if ( !(this instanceof jQuery.Event) ) {
            return new jQuery.Event( src, props );
        }
    
        // Event object
        if ( src && src.type ) {
            this.originalEvent = src;
            this.type = src.type;
    
            // Events bubbling up the document may have been marked as prevented
            // by a handler lower down the tree; reflect the correct value.
            this.isDefaultPrevented = src.defaultPrevented ||
                    src.defaultPrevented === undefined &&
                    // Support: Android < 4.0
                    src.returnValue === false ?
                returnTrue :
                returnFalse;
    
        // Event type
        } else {
            this.type = src;
        }
    
        // Put explicitly provided properties onto the event object
        if ( props ) {
            jQuery.extend( this, props );
        }
    
        // Create a timestamp if incoming event doesn't have one
        this.timeStamp = src && src.timeStamp || jQuery.now();
    
        // Mark it as fixed
        this[ jQuery.expando ] = true;
    };
    
    // jQuery.Event is based on DOM3 Events as specified by the ECMAScript Language Binding
    // http://www.w3.org/TR/2003/WD-DOM-Level-3-Events-20030331/ecma-script-binding.html
    jQuery.Event.prototype = {
        isDefaultPrevented: returnFalse,
        isPropagationStopped: returnFalse,
        isImmediatePropagationStopped: returnFalse,
    
        preventDefault: function() {
            var e = this.originalEvent;
    
            this.isDefaultPrevented = returnTrue;
    
            if ( e && e.preventDefault ) {
                e.preventDefault();
            }
        },
        stopPropagation: function() {
            var e = this.originalEvent;
    
            this.isPropagationStopped = returnTrue;
    
            if ( e && e.stopPropagation ) {
                e.stopPropagation();
            }
        },
        stopImmediatePropagation: function() {
            var e = this.originalEvent;
    
            this.isImmediatePropagationStopped = returnTrue;
    
            if ( e && e.stopImmediatePropagation ) {
                e.stopImmediatePropagation();
            }
    
            this.stopPropagation();
        }
    };
    
    // Create mouseenter/leave events using mouseover/out and event-time checks
    // Support: Chrome 15+
    jQuery.each({
        mouseenter: "mouseover",
        mouseleave: "mouseout",
        pointerenter: "pointerover",
        pointerleave: "pointerout"
    }, function( orig, fix ) {
        jQuery.event.special[ orig ] = {
            delegateType: fix,
            bindType: fix,
    
            handle: function( event ) {
                var ret,
                    target = this,
                    related = event.relatedTarget,
                    handleObj = event.handleObj;
    
                // For mousenter/leave call the handler if related is outside the target.
                // NB: No relatedTarget if the mouse left/entered the browser window
                if ( !related || (related !== target && !jQuery.contains( target, related )) ) {
                    event.type = handleObj.origType;
                    ret = handleObj.handler.apply( this, arguments );
                    event.type = fix;
                }
                return ret;
            }
        };
    });
    
    // Create "bubbling" focus and blur events
    // Support: Firefox, Chrome, Safari
    if ( !support.focusinBubbles ) {
        jQuery.each({ focus: "focusin", blur: "focusout" }, function( orig, fix ) {
    
            // Attach a single capturing handler on the document while someone wants focusin/focusout
            var handler = function( event ) {
                    jQuery.event.simulate( fix, event.target, jQuery.event.fix( event ), true );
                };
    
            jQuery.event.special[ fix ] = {
                setup: function() {
                    var doc = this.ownerDocument || this,
                        attaches = data_priv.access( doc, fix );
    
                    if ( !attaches ) {
                        doc.addEventListener( orig, handler, true );
                    }
                    data_priv.access( doc, fix, ( attaches || 0 ) + 1 );
                },
                teardown: function() {
                    var doc = this.ownerDocument || this,
                        attaches = data_priv.access( doc, fix ) - 1;
    
                    if ( !attaches ) {
                        doc.removeEventListener( orig, handler, true );
                        data_priv.remove( doc, fix );
    
                    } else {
                        data_priv.access( doc, fix, attaches );
                    }
                }
            };
        });
    }
    
    jQuery.fn.extend({
    
        on: function( types, selector, data, fn, /*INTERNAL*/ one ) {
            var origFn, type;
    
            // Types can be a map of types/handlers
            if ( typeof types === "object" ) {
                // ( types-Object, selector, data )
                if ( typeof selector !== "string" ) {
                    // ( types-Object, data )
                    data = data || selector;
                    selector = undefined;
                }
                for ( type in types ) {
                    this.on( type, selector, data, types[ type ], one );
                }
                return this;
            }
    
            if ( data == null && fn == null ) {
                // ( types, fn )
                fn = selector;
                data = selector = undefined;
            } else if ( fn == null ) {
                if ( typeof selector === "string" ) {
                    // ( types, selector, fn )
                    fn = data;
                    data = undefined;
                } else {
                    // ( types, data, fn )
                    fn = data;
                    data = selector;
                    selector = undefined;
                }
            }
            if ( fn === false ) {
                fn = returnFalse;
            } else if ( !fn ) {
                return this;
            }
    
            if ( one === 1 ) {
                origFn = fn;
                fn = function( event ) {
                    // Can use an empty set, since event contains the info
                    jQuery().off( event );
                    return origFn.apply( this, arguments );
                };
                // Use same guid so caller can remove using origFn
                fn.guid = origFn.guid || ( origFn.guid = jQuery.guid++ );
            }
            return this.each( function() {
                jQuery.event.add( this, types, fn, data, selector );
            });
        },
        one: function( types, selector, data, fn ) {
            return this.on( types, selector, data, fn, 1 );
        },
        off: function( types, selector, fn ) {
            var handleObj, type;
            if ( types && types.preventDefault && types.handleObj ) {
                // ( event )  dispatched jQuery.Event
                handleObj = types.handleObj;
                jQuery( types.delegateTarget ).off(
                    handleObj.namespace ? handleObj.origType + "." + handleObj.namespace : handleObj.origType,
                    handleObj.selector,
                    handleObj.handler
                );
                return this;
            }
            if ( typeof types === "object" ) {
                // ( types-object [, selector] )
                for ( type in types ) {
                    this.off( type, selector, types[ type ] );
                }
                return this;
            }
            if ( selector === false || typeof selector === "function" ) {
                // ( types [, fn] )
                fn = selector;
                selector = undefined;
            }
            if ( fn === false ) {
                fn = returnFalse;
            }
            return this.each(function() {
                jQuery.event.remove( this, types, fn, selector );
            });
        },
    
        trigger: function( type, data ) {
            return this.each(function() {
                jQuery.event.trigger( type, data, this );
            });
        },
        triggerHandler: function( type, data ) {
            var elem = this[0];
            if ( elem ) {
                return jQuery.event.trigger( type, data, elem, true );
            }
        }
    });
    
    
    var
        rxhtmlTag = /<(?!area|br|col|embed|hr|img|input|link|meta|param)(([\w:]+)[^>]*)\/>/gi,
        rtagName = /<([\w:]+)/,
        rhtml = /<|&#?\w+;/,
        rnoInnerhtml = /<(?:script|style|link)/i,
        // checked="checked" or checked
        rchecked = /checked\s*(?:[^=]|=\s*.checked.)/i,
        rscriptType = /^$|\/(?:java|ecma)script/i,
        rscriptTypeMasked = /^true\/(.*)/,
        rcleanScript = /^\s*<!(?:\[CDATA\[|--)|(?:\]\]|--)>\s*$/g,
    
        // We have to close these tags to support XHTML (#13200)
        wrapMap = {
    
            // Support: IE 9
            option: [ 1, "<select multiple='multiple'>", "</select>" ],
    
            thead: [ 1, "<table>", "</table>" ],
            col: [ 2, "<table><colgroup>", "</colgroup></table>" ],
            tr: [ 2, "<table><tbody>", "</tbody></table>" ],
            td: [ 3, "<table><tbody><tr>", "</tr></tbody></table>" ],
    
            _default: [ 0, "", "" ]
        };
    
    // Support: IE 9
    wrapMap.optgroup = wrapMap.option;
    
    wrapMap.tbody = wrapMap.tfoot = wrapMap.colgroup = wrapMap.caption = wrapMap.thead;
    wrapMap.th = wrapMap.td;
    
    // Support: 1.x compatibility
    // Manipulating tables requires a tbody
    function manipulationTarget( elem, content ) {
        return jQuery.nodeName( elem, "table" ) &&
            jQuery.nodeName( content.nodeType !== 11 ? content : content.firstChild, "tr" ) ?
    
            elem.getElementsByTagName("tbody")[0] ||
                elem.appendChild( elem.ownerDocument.createElement("tbody") ) :
            elem;
    }
    
    // Replace/restore the type attribute of script elements for safe DOM manipulation
    function disableScript( elem ) {
        elem.type = (elem.getAttribute("type") !== null) + "/" + elem.type;
        return elem;
    }
    function restoreScript( elem ) {
        var match = rscriptTypeMasked.exec( elem.type );
    
        if ( match ) {
            elem.type = match[ 1 ];
        } else {
            elem.removeAttribute("type");
        }
    
        return elem;
    }
    
    // Mark scripts as having already been evaluated
    function setGlobalEval( elems, refElements ) {
        var i = 0,
            l = elems.length;
    
        for ( ; i < l; i++ ) {
            data_priv.set(
                elems[ i ], "globalEval", !refElements || data_priv.get( refElements[ i ], "globalEval" )
            );
        }
    }
    
    function cloneCopyEvent( src, dest ) {
        var i, l, type, pdataOld, pdataCur, udataOld, udataCur, events;
    
        if ( dest.nodeType !== 1 ) {
            return;
        }
    
        // 1. Copy private data: events, handlers, etc.
        if ( data_priv.hasData( src ) ) {
            pdataOld = data_priv.access( src );
            pdataCur = data_priv.set( dest, pdataOld );
            events = pdataOld.events;
    
            if ( events ) {
                delete pdataCur.handle;
                pdataCur.events = {};
    
                for ( type in events ) {
                    for ( i = 0, l = events[ type ].length; i < l; i++ ) {
                        jQuery.event.add( dest, type, events[ type ][ i ] );
                    }
                }
            }
        }
    
        // 2. Copy user data
        if ( data_user.hasData( src ) ) {
            udataOld = data_user.access( src );
            udataCur = jQuery.extend( {}, udataOld );
    
            data_user.set( dest, udataCur );
        }
    }
    
    function getAll( context, tag ) {
        var ret = context.getElementsByTagName ? context.getElementsByTagName( tag || "*" ) :
                context.querySelectorAll ? context.querySelectorAll( tag || "*" ) :
                [];
    
        return tag === undefined || tag && jQuery.nodeName( context, tag ) ?
            jQuery.merge( [ context ], ret ) :
            ret;
    }
    
    // Support: IE >= 9
    function fixInput( src, dest ) {
        var nodeName = dest.nodeName.toLowerCase();
    
        // Fails to persist the checked state of a cloned checkbox or radio button.
        if ( nodeName === "input" && rcheckableType.test( src.type ) ) {
            dest.checked = src.checked;
    
        // Fails to return the selected option to the default selected state when cloning options
        } else if ( nodeName === "input" || nodeName === "textarea" ) {
            dest.defaultValue = src.defaultValue;
        }
    }
    
    jQuery.extend({
        clone: function( elem, dataAndEvents, deepDataAndEvents ) {
            var i, l, srcElements, destElements,
                clone = elem.cloneNode( true ),
                inPage = jQuery.contains( elem.ownerDocument, elem );
    
            // Support: IE >= 9
            // Fix Cloning issues
            if ( !support.noCloneChecked && ( elem.nodeType === 1 || elem.nodeType === 11 ) &&
                    !jQuery.isXMLDoc( elem ) ) {
    
                // We eschew Sizzle here for performance reasons: http://jsperf.com/getall-vs-sizzle/2
                destElements = getAll( clone );
                srcElements = getAll( elem );
    
                for ( i = 0, l = srcElements.length; i < l; i++ ) {
                    fixInput( srcElements[ i ], destElements[ i ] );
                }
            }
    
            // Copy the events from the original to the clone
            if ( dataAndEvents ) {
                if ( deepDataAndEvents ) {
                    srcElements = srcElements || getAll( elem );
                    destElements = destElements || getAll( clone );
    
                    for ( i = 0, l = srcElements.length; i < l; i++ ) {
                        cloneCopyEvent( srcElements[ i ], destElements[ i ] );
                    }
                } else {
                    cloneCopyEvent( elem, clone );
                }
            }
    
            // Preserve script evaluation history
            destElements = getAll( clone, "script" );
            if ( destElements.length > 0 ) {
                setGlobalEval( destElements, !inPage && getAll( elem, "script" ) );
            }
    
            // Return the cloned set
            return clone;
        },
    
        buildFragment: function( elems, context, scripts, selection ) {
            var elem, tmp, tag, wrap, contains, j,
                fragment = context.createDocumentFragment(),
                nodes = [],
                i = 0,
                l = elems.length;
    
            for ( ; i < l; i++ ) {
                elem = elems[ i ];
    
                if ( elem || elem === 0 ) {
    
                    // Add nodes directly
                    if ( jQuery.type( elem ) === "object" ) {
                        // Support: QtWebKit
                        // jQuery.merge because push.apply(_, arraylike) throws
                        jQuery.merge( nodes, elem.nodeType ? [ elem ] : elem );
    
                    // Convert non-html into a text node
                    } else if ( !rhtml.test( elem ) ) {
                        nodes.push( context.createTextNode( elem ) );
    
                    // Convert html into DOM nodes
                    } else {
                        tmp = tmp || fragment.appendChild( context.createElement("div") );
    
                        // Deserialize a standard representation
                        tag = ( rtagName.exec( elem ) || [ "", "" ] )[ 1 ].toLowerCase();
                        wrap = wrapMap[ tag ] || wrapMap._default;
                        tmp.innerHTML = wrap[ 1 ] + elem.replace( rxhtmlTag, "<$1></$2>" ) + wrap[ 2 ];
    
                        // Descend through wrappers to the right content
                        j = wrap[ 0 ];
                        while ( j-- ) {
                            tmp = tmp.lastChild;
                        }
    
                        // Support: QtWebKit
                        // jQuery.merge because push.apply(_, arraylike) throws
                        jQuery.merge( nodes, tmp.childNodes );
    
                        // Remember the top-level container
                        tmp = fragment.firstChild;
    
                        // Fixes #12346
                        // Support: Webkit, IE
                        tmp.textContent = "";
                    }
                }
            }
    
            // Remove wrapper from fragment
            fragment.textContent = "";
    
            i = 0;
            while ( (elem = nodes[ i++ ]) ) {
    
                // #4087 - If origin and destination elements are the same, and this is
                // that element, do not do anything
                if ( selection && jQuery.inArray( elem, selection ) !== -1 ) {
                    continue;
                }
    
                contains = jQuery.contains( elem.ownerDocument, elem );
    
                // Append to fragment
                tmp = getAll( fragment.appendChild( elem ), "script" );
    
                // Preserve script evaluation history
                if ( contains ) {
                    setGlobalEval( tmp );
                }
    
                // Capture executables
                if ( scripts ) {
                    j = 0;
                    while ( (elem = tmp[ j++ ]) ) {
                        if ( rscriptType.test( elem.type || "" ) ) {
                            scripts.push( elem );
                        }
                    }
                }
            }
    
            return fragment;
        },
    
        cleanData: function( elems ) {
            var data, elem, type, key,
                special = jQuery.event.special,
                i = 0;
    
            for ( ; (elem = elems[ i ]) !== undefined; i++ ) {
                if ( jQuery.acceptData( elem ) ) {
                    key = elem[ data_priv.expando ];
    
                    if ( key && (data = data_priv.cache[ key ]) ) {
                        if ( data.events ) {
                            for ( type in data.events ) {
                                if ( special[ type ] ) {
                                    jQuery.event.remove( elem, type );
    
                                // This is a shortcut to avoid jQuery.event.remove's overhead
                                } else {
                                    jQuery.removeEvent( elem, type, data.handle );
                                }
                            }
                        }
                        if ( data_priv.cache[ key ] ) {
                            // Discard any remaining `private` data
                            delete data_priv.cache[ key ];
                        }
                    }
                }
                // Discard any remaining `user` data
                delete data_user.cache[ elem[ data_user.expando ] ];
            }
        }
    });
    
    jQuery.fn.extend({
        text: function( value ) {
            return access( this, function( value ) {
                return value === undefined ?
                    jQuery.text( this ) :
                    this.empty().each(function() {
                        if ( this.nodeType === 1 || this.nodeType === 11 || this.nodeType === 9 ) {
                            this.textContent = value;
                        }
                    });
            }, null, value, arguments.length );
        },
    
        append: function() {
            return this.domManip( arguments, function( elem ) {
                if ( this.nodeType === 1 || this.nodeType === 11 || this.nodeType === 9 ) {
                    var target = manipulationTarget( this, elem );
                    target.appendChild( elem );
                }
            });
        },
    
        prepend: function() {
            return this.domManip( arguments, function( elem ) {
                if ( this.nodeType === 1 || this.nodeType === 11 || this.nodeType === 9 ) {
                    var target = manipulationTarget( this, elem );
                    target.insertBefore( elem, target.firstChild );
                }
            });
        },
    
        before: function() {
            return this.domManip( arguments, function( elem ) {
                if ( this.parentNode ) {
                    this.parentNode.insertBefore( elem, this );
                }
            });
        },
    
        after: function() {
            return this.domManip( arguments, function( elem ) {
                if ( this.parentNode ) {
                    this.parentNode.insertBefore( elem, this.nextSibling );
                }
            });
        },
    
        remove: function( selector, keepData /* Internal Use Only */ ) {
            var elem,
                elems = selector ? jQuery.filter( selector, this ) : this,
                i = 0;
    
            for ( ; (elem = elems[i]) != null; i++ ) {
                if ( !keepData && elem.nodeType === 1 ) {
                    jQuery.cleanData( getAll( elem ) );
                }
    
                if ( elem.parentNode ) {
                    if ( keepData && jQuery.contains( elem.ownerDocument, elem ) ) {
                        setGlobalEval( getAll( elem, "script" ) );
                    }
                    elem.parentNode.removeChild( elem );
                }
            }
    
            return this;
        },
    
        empty: function() {
            var elem,
                i = 0;
    
            for ( ; (elem = this[i]) != null; i++ ) {
                if ( elem.nodeType === 1 ) {
    
                    // Prevent memory leaks
                    jQuery.cleanData( getAll( elem, false ) );
    
                    // Remove any remaining nodes
                    elem.textContent = "";
                }
            }
    
            return this;
        },
    
        clone: function( dataAndEvents, deepDataAndEvents ) {
            dataAndEvents = dataAndEvents == null ? false : dataAndEvents;
            deepDataAndEvents = deepDataAndEvents == null ? dataAndEvents : deepDataAndEvents;
    
            return this.map(function() {
                return jQuery.clone( this, dataAndEvents, deepDataAndEvents );
            });
        },
    
        html: function( value ) {
            return access( this, function( value ) {
                var elem = this[ 0 ] || {},
                    i = 0,
                    l = this.length;
    
                if ( value === undefined && elem.nodeType === 1 ) {
                    return elem.innerHTML;
                }
    
                // See if we can take a shortcut and just use innerHTML
                if ( typeof value === "string" && !rnoInnerhtml.test( value ) &&
                    !wrapMap[ ( rtagName.exec( value ) || [ "", "" ] )[ 1 ].toLowerCase() ] ) {
    
                    value = value.replace( rxhtmlTag, "<$1></$2>" );
    
                    try {
                        for ( ; i < l; i++ ) {
                            elem = this[ i ] || {};
    
                            // Remove element nodes and prevent memory leaks
                            if ( elem.nodeType === 1 ) {
                                jQuery.cleanData( getAll( elem, false ) );
                                elem.innerHTML = value;
                            }
                        }
    
                        elem = 0;
    
                    // If using innerHTML throws an exception, use the fallback method
                    } catch( e ) {}
                }
    
                if ( elem ) {
                    this.empty().append( value );
                }
            }, null, value, arguments.length );
        },
    
        replaceWith: function() {
            var arg = arguments[ 0 ];
    
            // Make the changes, replacing each context element with the new content
            this.domManip( arguments, function( elem ) {
                arg = this.parentNode;
    
                jQuery.cleanData( getAll( this ) );
    
                if ( arg ) {
                    arg.replaceChild( elem, this );
                }
            });
    
            // Force removal if there was no new content (e.g., from empty arguments)
            return arg && (arg.length || arg.nodeType) ? this : this.remove();
        },
    
        detach: function( selector ) {
            return this.remove( selector, true );
        },
    
        domManip: function( args, callback ) {
    
            // Flatten any nested arrays
            args = concat.apply( [], args );
    
            var fragment, first, scripts, hasScripts, node, doc,
                i = 0,
                l = this.length,
                set = this,
                iNoClone = l - 1,
                value = args[ 0 ],
                isFunction = jQuery.isFunction( value );
    
            // We can't cloneNode fragments that contain checked, in WebKit
            if ( isFunction ||
                    ( l > 1 && typeof value === "string" &&
                        !support.checkClone && rchecked.test( value ) ) ) {
                return this.each(function( index ) {
                    var self = set.eq( index );
                    if ( isFunction ) {
                        args[ 0 ] = value.call( this, index, self.html() );
                    }
                    self.domManip( args, callback );
                });
            }
    
            if ( l ) {
                fragment = jQuery.buildFragment( args, this[ 0 ].ownerDocument, false, this );
                first = fragment.firstChild;
    
                if ( fragment.childNodes.length === 1 ) {
                    fragment = first;
                }
    
                if ( first ) {
                    scripts = jQuery.map( getAll( fragment, "script" ), disableScript );
                    hasScripts = scripts.length;
    
                    // Use the original fragment for the last item instead of the first because it can end up
                    // being emptied incorrectly in certain situations (#8070).
                    for ( ; i < l; i++ ) {
                        node = fragment;
    
                        if ( i !== iNoClone ) {
                            node = jQuery.clone( node, true, true );
    
                            // Keep references to cloned scripts for later restoration
                            if ( hasScripts ) {
                                // Support: QtWebKit
                                // jQuery.merge because push.apply(_, arraylike) throws
                                jQuery.merge( scripts, getAll( node, "script" ) );
                            }
                        }
    
                        callback.call( this[ i ], node, i );
                    }
    
                    if ( hasScripts ) {
                        doc = scripts[ scripts.length - 1 ].ownerDocument;
    
                        // Reenable scripts
                        jQuery.map( scripts, restoreScript );
    
                        // Evaluate executable scripts on first document insertion
                        for ( i = 0; i < hasScripts; i++ ) {
                            node = scripts[ i ];
                            if ( rscriptType.test( node.type || "" ) &&
                                !data_priv.access( node, "globalEval" ) && jQuery.contains( doc, node ) ) {
    
                                if ( node.src ) {
                                    // Optional AJAX dependency, but won't run scripts if not present
                                    if ( jQuery._evalUrl ) {
                                        jQuery._evalUrl( node.src );
                                    }
                                } else {
                                    jQuery.globalEval( node.textContent.replace( rcleanScript, "" ) );
                                }
                            }
                        }
                    }
                }
            }
    
            return this;
        }
    });
    
    jQuery.each({
        appendTo: "append",
        prependTo: "prepend",
        insertBefore: "before",
        insertAfter: "after",
        replaceAll: "replaceWith"
    }, function( name, original ) {
        jQuery.fn[ name ] = function( selector ) {
            var elems,
                ret = [],
                insert = jQuery( selector ),
                last = insert.length - 1,
                i = 0;
    
            for ( ; i <= last; i++ ) {
                elems = i === last ? this : this.clone( true );
                jQuery( insert[ i ] )[ original ]( elems );
    
                // Support: QtWebKit
                // .get() because push.apply(_, arraylike) throws
                push.apply( ret, elems.get() );
            }
    
            return this.pushStack( ret );
        };
    });
    
    
    var iframe,
        elemdisplay = {};
    
    /**
     * Retrieve the actual display of a element
     * @param {String} name nodeName of the element
     * @param {Object} doc Document object
     */
    // Called only from within defaultDisplay
    function actualDisplay( name, doc ) {
        var style,
            elem = jQuery( doc.createElement( name ) ).appendTo( doc.body ),
    
            // getDefaultComputedStyle might be reliably used only on attached element
            display = window.getDefaultComputedStyle && ( style = window.getDefaultComputedStyle( elem[ 0 ] ) ) ?
    
                // Use of this method is a temporary fix (more like optmization) until something better comes along,
                // since it was removed from specification and supported only in FF
                style.display : jQuery.css( elem[ 0 ], "display" );
    
        // We don't have any data stored on the element,
        // so use "detach" method as fast way to get rid of the element
        elem.detach();
    
        return display;
    }
    
    /**
     * Try to determine the default display value of an element
     * @param {String} nodeName
     */
    function defaultDisplay( nodeName ) {
        var doc = document,
            display = elemdisplay[ nodeName ];
    
        if ( !display ) {
            display = actualDisplay( nodeName, doc );
    
            // If the simple way fails, read from inside an iframe
            if ( display === "none" || !display ) {
    
                // Use the already-created iframe if possible
                iframe = (iframe || jQuery( "<iframe frameborder='0' width='0' height='0'/>" )).appendTo( doc.documentElement );
    
                // Always write a new HTML skeleton so Webkit and Firefox don't choke on reuse
                doc = iframe[ 0 ].contentDocument;
    
                // Support: IE
                doc.write();
                doc.close();
    
                display = actualDisplay( nodeName, doc );
                iframe.detach();
            }
    
            // Store the correct default display
            elemdisplay[ nodeName ] = display;
        }
    
        return display;
    }
    var rmargin = (/^margin/);
    
    var rnumnonpx = new RegExp( "^(" + pnum + ")(?!px)[a-z%]+$", "i" );
    
    var getStyles = function( elem ) {
            return elem.ownerDocument.defaultView.getComputedStyle( elem, null );
        };
    
    
    
    function curCSS( elem, name, computed ) {
        var width, minWidth, maxWidth, ret,
            style = elem.style;
    
        computed = computed || getStyles( elem );
    
        // Support: IE9
        // getPropertyValue is only needed for .css('filter') in IE9, see #12537
        if ( computed ) {
            ret = computed.getPropertyValue( name ) || computed[ name ];
        }
    
        if ( computed ) {
    
            if ( ret === "" && !jQuery.contains( elem.ownerDocument, elem ) ) {
                ret = jQuery.style( elem, name );
            }
    
            // Support: iOS < 6
            // A tribute to the "awesome hack by Dean Edwards"
            // iOS < 6 (at least) returns percentage for a larger set of values, but width seems to be reliably pixels
            // this is against the CSSOM draft spec: http://dev.w3.org/csswg/cssom/#resolved-values
            if ( rnumnonpx.test( ret ) && rmargin.test( name ) ) {
    
                // Remember the original values
                width = style.width;
                minWidth = style.minWidth;
                maxWidth = style.maxWidth;
    
                // Put in the new values to get a computed value out
                style.minWidth = style.maxWidth = style.width = ret;
                ret = computed.width;
    
                // Revert the changed values
                style.width = width;
                style.minWidth = minWidth;
                style.maxWidth = maxWidth;
            }
        }
    
        return ret !== undefined ?
            // Support: IE
            // IE returns zIndex value as an integer.
            ret + "" :
            ret;
    }
    
    
    function addGetHookIf( conditionFn, hookFn ) {
        // Define the hook, we'll check on the first run if it's really needed.
        return {
            get: function() {
                if ( conditionFn() ) {
                    // Hook not needed (or it's not possible to use it due to missing dependency),
                    // remove it.
                    // Since there are no other hooks for marginRight, remove the whole object.
                    delete this.get;
                    return;
                }
    
                // Hook needed; redefine it so that the support test is not executed again.
    
                return (this.get = hookFn).apply( this, arguments );
            }
        };
    }
    
    
    (function() {
        var pixelPositionVal, boxSizingReliableVal,
            docElem = document.documentElement,
            container = document.createElement( "div" ),
            div = document.createElement( "div" );
    
        if ( !div.style ) {
            return;
        }
    
        div.style.backgroundClip = "content-box";
        div.cloneNode( true ).style.backgroundClip = "";
        support.clearCloneStyle = div.style.backgroundClip === "content-box";
    
        container.style.cssText = "border:0;width:0;height:0;top:0;left:-9999px;margin-top:1px;" +
            "position:absolute";
        container.appendChild( div );
    
        // Executing both pixelPosition & boxSizingReliable tests require only one layout
        // so they're executed at the same time to save the second computation.
        function computePixelPositionAndBoxSizingReliable() {
            div.style.cssText =
                // Support: Firefox<29, Android 2.3
                // Vendor-prefix box-sizing
                "-webkit-box-sizing:border-box;-moz-box-sizing:border-box;" +
                "box-sizing:border-box;display:block;margin-top:1%;top:1%;" +
                "border:1px;padding:1px;width:4px;position:absolute";
            div.innerHTML = "";
            docElem.appendChild( container );
    
            var divStyle = window.getComputedStyle( div, null );
            pixelPositionVal = divStyle.top !== "1%";
            boxSizingReliableVal = divStyle.width === "4px";
    
            docElem.removeChild( container );
        }
    
        // Support: node.js jsdom
        // Don't assume that getComputedStyle is a property of the global object
        if ( window.getComputedStyle ) {
            jQuery.extend( support, {
                pixelPosition: function() {
                    // This test is executed only once but we still do memoizing
                    // since we can use the boxSizingReliable pre-computing.
                    // No need to check if the test was already performed, though.
                    computePixelPositionAndBoxSizingReliable();
                    return pixelPositionVal;
                },
                boxSizingReliable: function() {
                    if ( boxSizingReliableVal == null ) {
                        computePixelPositionAndBoxSizingReliable();
                    }
                    return boxSizingReliableVal;
                },
                reliableMarginRight: function() {
                    // Support: Android 2.3
                    // Check if div with explicit width and no margin-right incorrectly
                    // gets computed margin-right based on width of container. (#3333)
                    // WebKit Bug 13343 - getComputedStyle returns wrong value for margin-right
                    // This support function is only executed once so no memoizing is needed.
                    var ret,
                        marginDiv = div.appendChild( document.createElement( "div" ) );
    
                    // Reset CSS: box-sizing; display; margin; border; padding
                    marginDiv.style.cssText = div.style.cssText =
                        // Support: Firefox<29, Android 2.3
                        // Vendor-prefix box-sizing
                        "-webkit-box-sizing:content-box;-moz-box-sizing:content-box;" +
                        "box-sizing:content-box;display:block;margin:0;border:0;padding:0";
                    marginDiv.style.marginRight = marginDiv.style.width = "0";
                    div.style.width = "1px";
                    docElem.appendChild( container );
    
                    ret = !parseFloat( window.getComputedStyle( marginDiv, null ).marginRight );
    
                    docElem.removeChild( container );
    
                    return ret;
                }
            });
        }
    })();
    
    
    // A method for quickly swapping in/out CSS properties to get correct calculations.
    jQuery.swap = function( elem, options, callback, args ) {
        var ret, name,
            old = {};
    
        // Remember the old values, and insert the new ones
        for ( name in options ) {
            old[ name ] = elem.style[ name ];
            elem.style[ name ] = options[ name ];
        }
    
        ret = callback.apply( elem, args || [] );
    
        // Revert the old values
        for ( name in options ) {
            elem.style[ name ] = old[ name ];
        }
    
        return ret;
    };
    
    
    var
        // swappable if display is none or starts with table except "table", "table-cell", or "table-caption"
        // see here for display values: https://developer.mozilla.org/en-US/docs/CSS/display
        rdisplayswap = /^(none|table(?!-c[ea]).+)/,
        rnumsplit = new RegExp( "^(" + pnum + ")(.*)$", "i" ),
        rrelNum = new RegExp( "^([+-])=(" + pnum + ")", "i" ),
    
        cssShow = { position: "absolute", visibility: "hidden", display: "block" },
        cssNormalTransform = {
            letterSpacing: "0",
            fontWeight: "400"
        },
    
        cssPrefixes = [ "Webkit", "O", "Moz", "ms" ];
    
    // return a css property mapped to a potentially vendor prefixed property
    function vendorPropName( style, name ) {
    
        // shortcut for names that are not vendor prefixed
        if ( name in style ) {
            return name;
        }
    
        // check for vendor prefixed names
        var capName = name[0].toUpperCase() + name.slice(1),
            origName = name,
            i = cssPrefixes.length;
    
        while ( i-- ) {
            name = cssPrefixes[ i ] + capName;
            if ( name in style ) {
                return name;
            }
        }
    
        return origName;
    }
    
    function setPositiveNumber( elem, value, subtract ) {
        var matches = rnumsplit.exec( value );
        return matches ?
            // Guard against undefined "subtract", e.g., when used as in cssHooks
            Math.max( 0, matches[ 1 ] - ( subtract || 0 ) ) + ( matches[ 2 ] || "px" ) :
            value;
    }
    
    function augmentWidthOrHeight( elem, name, extra, isBorderBox, styles ) {
        var i = extra === ( isBorderBox ? "border" : "content" ) ?
            // If we already have the right measurement, avoid augmentation
            4 :
            // Otherwise initialize for horizontal or vertical properties
            name === "width" ? 1 : 0,
    
            val = 0;
    
        for ( ; i < 4; i += 2 ) {
            // both box models exclude margin, so add it if we want it
            if ( extra === "margin" ) {
                val += jQuery.css( elem, extra + cssExpand[ i ], true, styles );
            }
    
            if ( isBorderBox ) {
                // border-box includes padding, so remove it if we want content
                if ( extra === "content" ) {
                    val -= jQuery.css( elem, "padding" + cssExpand[ i ], true, styles );
                }
    
                // at this point, extra isn't border nor margin, so remove border
                if ( extra !== "margin" ) {
                    val -= jQuery.css( elem, "border" + cssExpand[ i ] + "Width", true, styles );
                }
            } else {
                // at this point, extra isn't content, so add padding
                val += jQuery.css( elem, "padding" + cssExpand[ i ], true, styles );
    
                // at this point, extra isn't content nor padding, so add border
                if ( extra !== "padding" ) {
                    val += jQuery.css( elem, "border" + cssExpand[ i ] + "Width", true, styles );
                }
            }
        }
    
        return val;
    }
    
    function getWidthOrHeight( elem, name, extra ) {
    
        // Start with offset property, which is equivalent to the border-box value
        var valueIsBorderBox = true,
            val = name === "width" ? elem.offsetWidth : elem.offsetHeight,
            styles = getStyles( elem ),
            isBorderBox = jQuery.css( elem, "boxSizing", false, styles ) === "border-box";
    
        // some non-html elements return undefined for offsetWidth, so check for null/undefined
        // svg - https://bugzilla.mozilla.org/show_bug.cgi?id=649285
        // MathML - https://bugzilla.mozilla.org/show_bug.cgi?id=491668
        if ( val <= 0 || val == null ) {
            // Fall back to computed then uncomputed css if necessary
            val = curCSS( elem, name, styles );
            if ( val < 0 || val == null ) {
                val = elem.style[ name ];
            }
    
            // Computed unit is not pixels. Stop here and return.
            if ( rnumnonpx.test(val) ) {
                return val;
            }
    
            // we need the check for style in case a browser which returns unreliable values
            // for getComputedStyle silently falls back to the reliable elem.style
            valueIsBorderBox = isBorderBox &&
                ( support.boxSizingReliable() || val === elem.style[ name ] );
    
            // Normalize "", auto, and prepare for extra
            val = parseFloat( val ) || 0;
        }
    
        // use the active box-sizing model to add/subtract irrelevant styles
        return ( val +
            augmentWidthOrHeight(
                elem,
                name,
                extra || ( isBorderBox ? "border" : "content" ),
                valueIsBorderBox,
                styles
            )
        ) + "px";
    }
    
    function showHide( elements, show ) {
        var display, elem, hidden,
            values = [],
            index = 0,
            length = elements.length;
    
        for ( ; index < length; index++ ) {
            elem = elements[ index ];
            if ( !elem.style ) {
                continue;
            }
    
            values[ index ] = data_priv.get( elem, "olddisplay" );
            display = elem.style.display;
            if ( show ) {
                // Reset the inline display of this element to learn if it is
                // being hidden by cascaded rules or not
                if ( !values[ index ] && display === "none" ) {
                    elem.style.display = "";
                }
    
                // Set elements which have been overridden with display: none
                // in a stylesheet to whatever the default browser style is
                // for such an element
                if ( elem.style.display === "" && isHidden( elem ) ) {
                    values[ index ] = data_priv.access( elem, "olddisplay", defaultDisplay(elem.nodeName) );
                }
            } else {
                hidden = isHidden( elem );
    
                if ( display !== "none" || !hidden ) {
                    data_priv.set( elem, "olddisplay", hidden ? display : jQuery.css( elem, "display" ) );
                }
            }
        }
    
        // Set the display of most of the elements in a second loop
        // to avoid the constant reflow
        for ( index = 0; index < length; index++ ) {
            elem = elements[ index ];
            if ( !elem.style ) {
                continue;
            }
            if ( !show || elem.style.display === "none" || elem.style.display === "" ) {
                elem.style.display = show ? values[ index ] || "" : "none";
            }
        }
    
        return elements;
    }
    
    jQuery.extend({
        // Add in style property hooks for overriding the default
        // behavior of getting and setting a style property
        cssHooks: {
            opacity: {
                get: function( elem, computed ) {
                    if ( computed ) {
                        // We should always get a number back from opacity
                        var ret = curCSS( elem, "opacity" );
                        return ret === "" ? "1" : ret;
                    }
                }
            }
        },
    
        // Don't automatically add "px" to these possibly-unitless properties
        cssNumber: {
            "columnCount": true,
            "fillOpacity": true,
            "flexGrow": true,
            "flexShrink": true,
            "fontWeight": true,
            "lineHeight": true,
            "opacity": true,
            "order": true,
            "orphans": true,
            "widows": true,
            "zIndex": true,
            "zoom": true
        },
    
        // Add in properties whose names you wish to fix before
        // setting or getting the value
        cssProps: {
            // normalize float css property
            "float": "cssFloat"
        },
    
        // Get and set the style property on a DOM Node
        style: function( elem, name, value, extra ) {
            // Don't set styles on text and comment nodes
            if ( !elem || elem.nodeType === 3 || elem.nodeType === 8 || !elem.style ) {
                return;
            }
    
            // Make sure that we're working with the right name
            var ret, type, hooks,
                origName = jQuery.camelCase( name ),
                style = elem.style;
    
            name = jQuery.cssProps[ origName ] || ( jQuery.cssProps[ origName ] = vendorPropName( style, origName ) );
    
            // gets hook for the prefixed version
            // followed by the unprefixed version
            hooks = jQuery.cssHooks[ name ] || jQuery.cssHooks[ origName ];
    
            // Check if we're setting a value
            if ( value !== undefined ) {
                type = typeof value;
    
                // convert relative number strings (+= or -=) to relative numbers. #7345
                if ( type === "string" && (ret = rrelNum.exec( value )) ) {
                    value = ( ret[1] + 1 ) * ret[2] + parseFloat( jQuery.css( elem, name ) );
                    // Fixes bug #9237
                    type = "number";
                }
    
                // Make sure that null and NaN values aren't set. See: #7116
                if ( value == null || value !== value ) {
                    return;
                }
    
                // If a number was passed in, add 'px' to the (except for certain CSS properties)
                if ( type === "number" && !jQuery.cssNumber[ origName ] ) {
                    value += "px";
                }
    
                // Fixes #8908, it can be done more correctly by specifying setters in cssHooks,
                // but it would mean to define eight (for every problematic property) identical functions
                if ( !support.clearCloneStyle && value === "" && name.indexOf( "background" ) === 0 ) {
                    style[ name ] = "inherit";
                }
    
                // If a hook was provided, use that value, otherwise just set the specified value
                if ( !hooks || !("set" in hooks) || (value = hooks.set( elem, value, extra )) !== undefined ) {
                    style[ name ] = value;
                }
    
            } else {
                // If a hook was provided get the non-computed value from there
                if ( hooks && "get" in hooks && (ret = hooks.get( elem, false, extra )) !== undefined ) {
                    return ret;
                }
    
                // Otherwise just get the value from the style object
                return style[ name ];
            }
        },
    
        css: function( elem, name, extra, styles ) {
            var val, num, hooks,
                origName = jQuery.camelCase( name );
    
            // Make sure that we're working with the right name
            name = jQuery.cssProps[ origName ] || ( jQuery.cssProps[ origName ] = vendorPropName( elem.style, origName ) );
    
            // gets hook for the prefixed version
            // followed by the unprefixed version
            hooks = jQuery.cssHooks[ name ] || jQuery.cssHooks[ origName ];
    
            // If a hook was provided get the computed value from there
            if ( hooks && "get" in hooks ) {
                val = hooks.get( elem, true, extra );
            }
    
            // Otherwise, if a way to get the computed value exists, use that
            if ( val === undefined ) {
                val = curCSS( elem, name, styles );
            }
    
            //convert "normal" to computed value
            if ( val === "normal" && name in cssNormalTransform ) {
                val = cssNormalTransform[ name ];
            }
    
            // Return, converting to number if forced or a qualifier was provided and val looks numeric
            if ( extra === "" || extra ) {
                num = parseFloat( val );
                return extra === true || jQuery.isNumeric( num ) ? num || 0 : val;
            }
            return val;
        }
    });
    
    jQuery.each([ "height", "width" ], function( i, name ) {
        jQuery.cssHooks[ name ] = {
            get: function( elem, computed, extra ) {
                if ( computed ) {
                    // certain elements can have dimension info if we invisibly show them
                    // however, it must have a current display style that would benefit from this
                    return rdisplayswap.test( jQuery.css( elem, "display" ) ) && elem.offsetWidth === 0 ?
                        jQuery.swap( elem, cssShow, function() {
                            return getWidthOrHeight( elem, name, extra );
                        }) :
                        getWidthOrHeight( elem, name, extra );
                }
            },
    
            set: function( elem, value, extra ) {
                var styles = extra && getStyles( elem );
                return setPositiveNumber( elem, value, extra ?
                    augmentWidthOrHeight(
                        elem,
                        name,
                        extra,
                        jQuery.css( elem, "boxSizing", false, styles ) === "border-box",
                        styles
                    ) : 0
                );
            }
        };
    });
    
    // Support: Android 2.3
    jQuery.cssHooks.marginRight = addGetHookIf( support.reliableMarginRight,
        function( elem, computed ) {
            if ( computed ) {
                // WebKit Bug 13343 - getComputedStyle returns wrong value for margin-right
                // Work around by temporarily setting element display to inline-block
                return jQuery.swap( elem, { "display": "inline-block" },
                    curCSS, [ elem, "marginRight" ] );
            }
        }
    );
    
    // These hooks are used by animate to expand properties
    jQuery.each({
        margin: "",
        padding: "",
        border: "Width"
    }, function( prefix, suffix ) {
        jQuery.cssHooks[ prefix + suffix ] = {
            expand: function( value ) {
                var i = 0,
                    expanded = {},
    
                    // assumes a single number if not a string
                    parts = typeof value === "string" ? value.split(" ") : [ value ];
    
                for ( ; i < 4; i++ ) {
                    expanded[ prefix + cssExpand[ i ] + suffix ] =
                        parts[ i ] || parts[ i - 2 ] || parts[ 0 ];
                }
    
                return expanded;
            }
        };
    
        if ( !rmargin.test( prefix ) ) {
            jQuery.cssHooks[ prefix + suffix ].set = setPositiveNumber;
        }
    });
    
    jQuery.fn.extend({
        css: function( name, value ) {
            return access( this, function( elem, name, value ) {
                var styles, len,
                    map = {},
                    i = 0;
    
                if ( jQuery.isArray( name ) ) {
                    styles = getStyles( elem );
                    len = name.length;
    
                    for ( ; i < len; i++ ) {
                        map[ name[ i ] ] = jQuery.css( elem, name[ i ], false, styles );
                    }
    
                    return map;
                }
    
                return value !== undefined ?
                    jQuery.style( elem, name, value ) :
                    jQuery.css( elem, name );
            }, name, value, arguments.length > 1 );
        },
        show: function() {
            return showHide( this, true );
        },
        hide: function() {
            return showHide( this );
        },
        toggle: function( state ) {
            if ( typeof state === "boolean" ) {
                return state ? this.show() : this.hide();
            }
    
            return this.each(function() {
                if ( isHidden( this ) ) {
                    jQuery( this ).show();
                } else {
                    jQuery( this ).hide();
                }
            });
        }
    });
    
    
    function Tween( elem, options, prop, end, easing ) {
        return new Tween.prototype.init( elem, options, prop, end, easing );
    }
    jQuery.Tween = Tween;
    
    Tween.prototype = {
        constructor: Tween,
        init: function( elem, options, prop, end, easing, unit ) {
            this.elem = elem;
            this.prop = prop;
            this.easing = easing || "swing";
            this.options = options;
            this.start = this.now = this.cur();
            this.end = end;
            this.unit = unit || ( jQuery.cssNumber[ prop ] ? "" : "px" );
        },
        cur: function() {
            var hooks = Tween.propHooks[ this.prop ];
    
            return hooks && hooks.get ?
                hooks.get( this ) :
                Tween.propHooks._default.get( this );
        },
        run: function( percent ) {
            var eased,
                hooks = Tween.propHooks[ this.prop ];
    
            if ( this.options.duration ) {
                this.pos = eased = jQuery.easing[ this.easing ](
                    percent, this.options.duration * percent, 0, 1, this.options.duration
                );
            } else {
                this.pos = eased = percent;
            }
            this.now = ( this.end - this.start ) * eased + this.start;
    
            if ( this.options.step ) {
                this.options.step.call( this.elem, this.now, this );
            }
    
            if ( hooks && hooks.set ) {
                hooks.set( this );
            } else {
                Tween.propHooks._default.set( this );
            }
            return this;
        }
    };
    
    Tween.prototype.init.prototype = Tween.prototype;
    
    Tween.propHooks = {
        _default: {
            get: function( tween ) {
                var result;
    
                if ( tween.elem[ tween.prop ] != null &&
                    (!tween.elem.style || tween.elem.style[ tween.prop ] == null) ) {
                    return tween.elem[ tween.prop ];
                }
    
                // passing an empty string as a 3rd parameter to .css will automatically
                // attempt a parseFloat and fallback to a string if the parse fails
                // so, simple values such as "10px" are parsed to Float.
                // complex values such as "rotate(1rad)" are returned as is.
                result = jQuery.css( tween.elem, tween.prop, "" );
                // Empty strings, null, undefined and "auto" are converted to 0.
                return !result || result === "auto" ? 0 : result;
            },
            set: function( tween ) {
                // use step hook for back compat - use cssHook if its there - use .style if its
                // available and use plain properties where available
                if ( jQuery.fx.step[ tween.prop ] ) {
                    jQuery.fx.step[ tween.prop ]( tween );
                } else if ( tween.elem.style && ( tween.elem.style[ jQuery.cssProps[ tween.prop ] ] != null || jQuery.cssHooks[ tween.prop ] ) ) {
                    jQuery.style( tween.elem, tween.prop, tween.now + tween.unit );
                } else {
                    tween.elem[ tween.prop ] = tween.now;
                }
            }
        }
    };
    
    // Support: IE9
    // Panic based approach to setting things on disconnected nodes
    
    Tween.propHooks.scrollTop = Tween.propHooks.scrollLeft = {
        set: function( tween ) {
            if ( tween.elem.nodeType && tween.elem.parentNode ) {
                tween.elem[ tween.prop ] = tween.now;
            }
        }
    };
    
    jQuery.easing = {
        linear: function( p ) {
            return p;
        },
        swing: function( p ) {
            return 0.5 - Math.cos( p * Math.PI ) / 2;
        }
    };
    
    jQuery.fx = Tween.prototype.init;
    
    // Back Compat <1.8 extension point
    jQuery.fx.step = {};
    
    
    
    
    var
        fxNow, timerId,
        rfxtypes = /^(?:toggle|show|hide)$/,
        rfxnum = new RegExp( "^(?:([+-])=|)(" + pnum + ")([a-z%]*)$", "i" ),
        rrun = /queueHooks$/,
        animationPrefilters = [ defaultPrefilter ],
        tweeners = {
            "*": [ function( prop, value ) {
                var tween = this.createTween( prop, value ),
                    target = tween.cur(),
                    parts = rfxnum.exec( value ),
                    unit = parts && parts[ 3 ] || ( jQuery.cssNumber[ prop ] ? "" : "px" ),
    
                    // Starting value computation is required for potential unit mismatches
                    start = ( jQuery.cssNumber[ prop ] || unit !== "px" && +target ) &&
                        rfxnum.exec( jQuery.css( tween.elem, prop ) ),
                    scale = 1,
                    maxIterations = 20;
    
                if ( start && start[ 3 ] !== unit ) {
                    // Trust units reported by jQuery.css
                    unit = unit || start[ 3 ];
    
                    // Make sure we update the tween properties later on
                    parts = parts || [];
    
                    // Iteratively approximate from a nonzero starting point
                    start = +target || 1;
    
                    do {
                        // If previous iteration zeroed out, double until we get *something*
                        // Use a string for doubling factor so we don't accidentally see scale as unchanged below
                        scale = scale || ".5";
    
                        // Adjust and apply
                        start = start / scale;
                        jQuery.style( tween.elem, prop, start + unit );
    
                    // Update scale, tolerating zero or NaN from tween.cur()
                    // And breaking the loop if scale is unchanged or perfect, or if we've just had enough
                    } while ( scale !== (scale = tween.cur() / target) && scale !== 1 && --maxIterations );
                }
    
                // Update tween properties
                if ( parts ) {
                    start = tween.start = +start || +target || 0;
                    tween.unit = unit;
                    // If a +=/-= token was provided, we're doing a relative animation
                    tween.end = parts[ 1 ] ?
                        start + ( parts[ 1 ] + 1 ) * parts[ 2 ] :
                        +parts[ 2 ];
                }
    
                return tween;
            } ]
        };
    
    // Animations created synchronously will run synchronously
    function createFxNow() {
        setTimeout(function() {
            fxNow = undefined;
        });
        return ( fxNow = jQuery.now() );
    }
    
    // Generate parameters to create a standard animation
    function genFx( type, includeWidth ) {
        var which,
            i = 0,
            attrs = { height: type };
    
        // if we include width, step value is 1 to do all cssExpand values,
        // if we don't include width, step value is 2 to skip over Left and Right
        includeWidth = includeWidth ? 1 : 0;
        for ( ; i < 4 ; i += 2 - includeWidth ) {
            which = cssExpand[ i ];
            attrs[ "margin" + which ] = attrs[ "padding" + which ] = type;
        }
    
        if ( includeWidth ) {
            attrs.opacity = attrs.width = type;
        }
    
        return attrs;
    }
    
    function createTween( value, prop, animation ) {
        var tween,
            collection = ( tweeners[ prop ] || [] ).concat( tweeners[ "*" ] ),
            index = 0,
            length = collection.length;
        for ( ; index < length; index++ ) {
            if ( (tween = collection[ index ].call( animation, prop, value )) ) {
    
                // we're done with this property
                return tween;
            }
        }
    }
    
    function defaultPrefilter( elem, props, opts ) {
        /* jshint validthis: true */
        var prop, value, toggle, tween, hooks, oldfire, display, checkDisplay,
            anim = this,
            orig = {},
            style = elem.style,
            hidden = elem.nodeType && isHidden( elem ),
            dataShow = data_priv.get( elem, "fxshow" );
    
        // handle queue: false promises
        if ( !opts.queue ) {
            hooks = jQuery._queueHooks( elem, "fx" );
            if ( hooks.unqueued == null ) {
                hooks.unqueued = 0;
                oldfire = hooks.empty.fire;
                hooks.empty.fire = function() {
                    if ( !hooks.unqueued ) {
                        oldfire();
                    }
                };
            }
            hooks.unqueued++;
    
            anim.always(function() {
                // doing this makes sure that the complete handler will be called
                // before this completes
                anim.always(function() {
                    hooks.unqueued--;
                    if ( !jQuery.queue( elem, "fx" ).length ) {
                        hooks.empty.fire();
                    }
                });
            });
        }
    
        // height/width overflow pass
        if ( elem.nodeType === 1 && ( "height" in props || "width" in props ) ) {
            // Make sure that nothing sneaks out
            // Record all 3 overflow attributes because IE9-10 do not
            // change the overflow attribute when overflowX and
            // overflowY are set to the same value
            opts.overflow = [ style.overflow, style.overflowX, style.overflowY ];
    
            // Set display property to inline-block for height/width
            // animations on inline elements that are having width/height animated
            display = jQuery.css( elem, "display" );
    
            // Test default display if display is currently "none"
            checkDisplay = display === "none" ?
                data_priv.get( elem, "olddisplay" ) || defaultDisplay( elem.nodeName ) : display;
    
            if ( checkDisplay === "inline" && jQuery.css( elem, "float" ) === "none" ) {
                style.display = "inline-block";
            }
        }
    
        if ( opts.overflow ) {
            style.overflow = "hidden";
            anim.always(function() {
                style.overflow = opts.overflow[ 0 ];
                style.overflowX = opts.overflow[ 1 ];
                style.overflowY = opts.overflow[ 2 ];
            });
        }
    
        // show/hide pass
        for ( prop in props ) {
            value = props[ prop ];
            if ( rfxtypes.exec( value ) ) {
                delete props[ prop ];
                toggle = toggle || value === "toggle";
                if ( value === ( hidden ? "hide" : "show" ) ) {
    
                    // If there is dataShow left over from a stopped hide or show and we are going to proceed with show, we should pretend to be hidden
                    if ( value === "show" && dataShow && dataShow[ prop ] !== undefined ) {
                        hidden = true;
                    } else {
                        continue;
                    }
                }
                orig[ prop ] = dataShow && dataShow[ prop ] || jQuery.style( elem, prop );
    
            // Any non-fx value stops us from restoring the original display value
            } else {
                display = undefined;
            }
        }
    
        if ( !jQuery.isEmptyObject( orig ) ) {
            if ( dataShow ) {
                if ( "hidden" in dataShow ) {
                    hidden = dataShow.hidden;
                }
            } else {
                dataShow = data_priv.access( elem, "fxshow", {} );
            }
    
            // store state if its toggle - enables .stop().toggle() to "reverse"
            if ( toggle ) {
                dataShow.hidden = !hidden;
            }
            if ( hidden ) {
                jQuery( elem ).show();
            } else {
                anim.done(function() {
                    jQuery( elem ).hide();
                });
            }
            anim.done(function() {
                var prop;
    
                data_priv.remove( elem, "fxshow" );
                for ( prop in orig ) {
                    jQuery.style( elem, prop, orig[ prop ] );
                }
            });
            for ( prop in orig ) {
                tween = createTween( hidden ? dataShow[ prop ] : 0, prop, anim );
    
                if ( !( prop in dataShow ) ) {
                    dataShow[ prop ] = tween.start;
                    if ( hidden ) {
                        tween.end = tween.start;
                        tween.start = prop === "width" || prop === "height" ? 1 : 0;
                    }
                }
            }
    
        // If this is a noop like .hide().hide(), restore an overwritten display value
        } else if ( (display === "none" ? defaultDisplay( elem.nodeName ) : display) === "inline" ) {
            style.display = display;
        }
    }
    
    function propFilter( props, specialEasing ) {
        var index, name, easing, value, hooks;
    
        // camelCase, specialEasing and expand cssHook pass
        for ( index in props ) {
            name = jQuery.camelCase( index );
            easing = specialEasing[ name ];
            value = props[ index ];
            if ( jQuery.isArray( value ) ) {
                easing = value[ 1 ];
                value = props[ index ] = value[ 0 ];
            }
    
            if ( index !== name ) {
                props[ name ] = value;
                delete props[ index ];
            }
    
            hooks = jQuery.cssHooks[ name ];
            if ( hooks && "expand" in hooks ) {
                value = hooks.expand( value );
                delete props[ name ];
    
                // not quite $.extend, this wont overwrite keys already present.
                // also - reusing 'index' from above because we have the correct "name"
                for ( index in value ) {
                    if ( !( index in props ) ) {
                        props[ index ] = value[ index ];
                        specialEasing[ index ] = easing;
                    }
                }
            } else {
                specialEasing[ name ] = easing;
            }
        }
    }
    
    function Animation( elem, properties, options ) {
        var result,
            stopped,
            index = 0,
            length = animationPrefilters.length,
            deferred = jQuery.Deferred().always( function() {
                // don't match elem in the :animated selector
                delete tick.elem;
            }),
            tick = function() {
                if ( stopped ) {
                    return false;
                }
                var currentTime = fxNow || createFxNow(),
                    remaining = Math.max( 0, animation.startTime + animation.duration - currentTime ),
                    // archaic crash bug won't allow us to use 1 - ( 0.5 || 0 ) (#12497)
                    temp = remaining / animation.duration || 0,
                    percent = 1 - temp,
                    index = 0,
                    length = animation.tweens.length;
    
                for ( ; index < length ; index++ ) {
                    animation.tweens[ index ].run( percent );
                }
    
                deferred.notifyWith( elem, [ animation, percent, remaining ]);
    
                if ( percent < 1 && length ) {
                    return remaining;
                } else {
                    deferred.resolveWith( elem, [ animation ] );
                    return false;
                }
            },
            animation = deferred.promise({
                elem: elem,
                props: jQuery.extend( {}, properties ),
                opts: jQuery.extend( true, { specialEasing: {} }, options ),
                originalProperties: properties,
                originalOptions: options,
                startTime: fxNow || createFxNow(),
                duration: options.duration,
                tweens: [],
                createTween: function( prop, end ) {
                    var tween = jQuery.Tween( elem, animation.opts, prop, end,
                            animation.opts.specialEasing[ prop ] || animation.opts.easing );
                    animation.tweens.push( tween );
                    return tween;
                },
                stop: function( gotoEnd ) {
                    var index = 0,
                        // if we are going to the end, we want to run all the tweens
                        // otherwise we skip this part
                        length = gotoEnd ? animation.tweens.length : 0;
                    if ( stopped ) {
                        return this;
                    }
                    stopped = true;
                    for ( ; index < length ; index++ ) {
                        animation.tweens[ index ].run( 1 );
                    }
    
                    // resolve when we played the last frame
                    // otherwise, reject
                    if ( gotoEnd ) {
                        deferred.resolveWith( elem, [ animation, gotoEnd ] );
                    } else {
                        deferred.rejectWith( elem, [ animation, gotoEnd ] );
                    }
                    return this;
                }
            }),
            props = animation.props;
    
        propFilter( props, animation.opts.specialEasing );
    
        for ( ; index < length ; index++ ) {
            result = animationPrefilters[ index ].call( animation, elem, props, animation.opts );
            if ( result ) {
                return result;
            }
        }
    
        jQuery.map( props, createTween, animation );
    
        if ( jQuery.isFunction( animation.opts.start ) ) {
            animation.opts.start.call( elem, animation );
        }
    
        jQuery.fx.timer(
            jQuery.extend( tick, {
                elem: elem,
                anim: animation,
                queue: animation.opts.queue
            })
        );
    
        // attach callbacks from options
        return animation.progress( animation.opts.progress )
            .done( animation.opts.done, animation.opts.complete )
            .fail( animation.opts.fail )
            .always( animation.opts.always );
    }
    
    jQuery.Animation = jQuery.extend( Animation, {
    
        tweener: function( props, callback ) {
            if ( jQuery.isFunction( props ) ) {
                callback = props;
                props = [ "*" ];
            } else {
                props = props.split(" ");
            }
    
            var prop,
                index = 0,
                length = props.length;
    
            for ( ; index < length ; index++ ) {
                prop = props[ index ];
                tweeners[ prop ] = tweeners[ prop ] || [];
                tweeners[ prop ].unshift( callback );
            }
        },
    
        prefilter: function( callback, prepend ) {
            if ( prepend ) {
                animationPrefilters.unshift( callback );
            } else {
                animationPrefilters.push( callback );
            }
        }
    });
    
    jQuery.speed = function( speed, easing, fn ) {
        var opt = speed && typeof speed === "object" ? jQuery.extend( {}, speed ) : {
            complete: fn || !fn && easing ||
                jQuery.isFunction( speed ) && speed,
            duration: speed,
            easing: fn && easing || easing && !jQuery.isFunction( easing ) && easing
        };
    
        opt.duration = jQuery.fx.off ? 0 : typeof opt.duration === "number" ? opt.duration :
            opt.duration in jQuery.fx.speeds ? jQuery.fx.speeds[ opt.duration ] : jQuery.fx.speeds._default;
    
        // normalize opt.queue - true/undefined/null -> "fx"
        if ( opt.queue == null || opt.queue === true ) {
            opt.queue = "fx";
        }
    
        // Queueing
        opt.old = opt.complete;
    
        opt.complete = function() {
            if ( jQuery.isFunction( opt.old ) ) {
                opt.old.call( this );
            }
    
            if ( opt.queue ) {
                jQuery.dequeue( this, opt.queue );
            }
        };
    
        return opt;
    };
    
    jQuery.fn.extend({
        fadeTo: function( speed, to, easing, callback ) {
    
            // show any hidden elements after setting opacity to 0
            return this.filter( isHidden ).css( "opacity", 0 ).show()
    
                // animate to the value specified
                .end().animate({ opacity: to }, speed, easing, callback );
        },
        animate: function( prop, speed, easing, callback ) {
            var empty = jQuery.isEmptyObject( prop ),
                optall = jQuery.speed( speed, easing, callback ),
                doAnimation = function() {
                    // Operate on a copy of prop so per-property easing won't be lost
                    var anim = Animation( this, jQuery.extend( {}, prop ), optall );
    
                    // Empty animations, or finishing resolves immediately
                    if ( empty || data_priv.get( this, "finish" ) ) {
                        anim.stop( true );
                    }
                };
                doAnimation.finish = doAnimation;
    
            return empty || optall.queue === false ?
                this.each( doAnimation ) :
                this.queue( optall.queue, doAnimation );
        },
        stop: function( type, clearQueue, gotoEnd ) {
            var stopQueue = function( hooks ) {
                var stop = hooks.stop;
                delete hooks.stop;
                stop( gotoEnd );
            };
    
            if ( typeof type !== "string" ) {
                gotoEnd = clearQueue;
                clearQueue = type;
                type = undefined;
            }
            if ( clearQueue && type !== false ) {
                this.queue( type || "fx", [] );
            }
    
            return this.each(function() {
                var dequeue = true,
                    index = type != null && type + "queueHooks",
                    timers = jQuery.timers,
                    data = data_priv.get( this );
    
                if ( index ) {
                    if ( data[ index ] && data[ index ].stop ) {
                        stopQueue( data[ index ] );
                    }
                } else {
                    for ( index in data ) {
                        if ( data[ index ] && data[ index ].stop && rrun.test( index ) ) {
                            stopQueue( data[ index ] );
                        }
                    }
                }
    
                for ( index = timers.length; index--; ) {
                    if ( timers[ index ].elem === this && (type == null || timers[ index ].queue === type) ) {
                        timers[ index ].anim.stop( gotoEnd );
                        dequeue = false;
                        timers.splice( index, 1 );
                    }
                }
    
                // start the next in the queue if the last step wasn't forced
                // timers currently will call their complete callbacks, which will dequeue
                // but only if they were gotoEnd
                if ( dequeue || !gotoEnd ) {
                    jQuery.dequeue( this, type );
                }
            });
        },
        finish: function( type ) {
            if ( type !== false ) {
                type = type || "fx";
            }
            return this.each(function() {
                var index,
                    data = data_priv.get( this ),
                    queue = data[ type + "queue" ],
                    hooks = data[ type + "queueHooks" ],
                    timers = jQuery.timers,
                    length = queue ? queue.length : 0;
    
                // enable finishing flag on private data
                data.finish = true;
    
                // empty the queue first
                jQuery.queue( this, type, [] );
    
                if ( hooks && hooks.stop ) {
                    hooks.stop.call( this, true );
                }
    
                // look for any active animations, and finish them
                for ( index = timers.length; index--; ) {
                    if ( timers[ index ].elem === this && timers[ index ].queue === type ) {
                        timers[ index ].anim.stop( true );
                        timers.splice( index, 1 );
                    }
                }
    
                // look for any animations in the old queue and finish them
                for ( index = 0; index < length; index++ ) {
                    if ( queue[ index ] && queue[ index ].finish ) {
                        queue[ index ].finish.call( this );
                    }
                }
    
                // turn off finishing flag
                delete data.finish;
            });
        }
    });
    
    jQuery.each([ "toggle", "show", "hide" ], function( i, name ) {
        var cssFn = jQuery.fn[ name ];
        jQuery.fn[ name ] = function( speed, easing, callback ) {
            return speed == null || typeof speed === "boolean" ?
                cssFn.apply( this, arguments ) :
                this.animate( genFx( name, true ), speed, easing, callback );
        };
    });
    
    // Generate shortcuts for custom animations
    jQuery.each({
        slideDown: genFx("show"),
        slideUp: genFx("hide"),
        slideToggle: genFx("toggle"),
        fadeIn: { opacity: "show" },
        fadeOut: { opacity: "hide" },
        fadeToggle: { opacity: "toggle" }
    }, function( name, props ) {
        jQuery.fn[ name ] = function( speed, easing, callback ) {
            return this.animate( props, speed, easing, callback );
        };
    });
    
    jQuery.timers = [];
    jQuery.fx.tick = function() {
        var timer,
            i = 0,
            timers = jQuery.timers;
    
        fxNow = jQuery.now();
    
        for ( ; i < timers.length; i++ ) {
            timer = timers[ i ];
            // Checks the timer has not already been removed
            if ( !timer() && timers[ i ] === timer ) {
                timers.splice( i--, 1 );
            }
        }
    
        if ( !timers.length ) {
            jQuery.fx.stop();
        }
        fxNow = undefined;
    };
    
    jQuery.fx.timer = function( timer ) {
        jQuery.timers.push( timer );
        if ( timer() ) {
            jQuery.fx.start();
        } else {
            jQuery.timers.pop();
        }
    };
    
    jQuery.fx.interval = 13;
    
    jQuery.fx.start = function() {
        if ( !timerId ) {
            timerId = setInterval( jQuery.fx.tick, jQuery.fx.interval );
        }
    };
    
    jQuery.fx.stop = function() {
        clearInterval( timerId );
        timerId = null;
    };
    
    jQuery.fx.speeds = {
        slow: 600,
        fast: 200,
        // Default speed
        _default: 400
    };
    
    
    // Based off of the plugin by Clint Helfers, with permission.
    // http://blindsignals.com/index.php/2009/07/jquery-delay/
    jQuery.fn.delay = function( time, type ) {
        time = jQuery.fx ? jQuery.fx.speeds[ time ] || time : time;
        type = type || "fx";
    
        return this.queue( type, function( next, hooks ) {
            var timeout = setTimeout( next, time );
            hooks.stop = function() {
                clearTimeout( timeout );
            };
        });
    };
    
    
    (function() {
        var input = document.createElement( "input" ),
            select = document.createElement( "select" ),
            opt = select.appendChild( document.createElement( "option" ) );
    
        input.type = "checkbox";
    
        // Support: iOS 5.1, Android 4.x, Android 2.3
        // Check the default checkbox/radio value ("" on old WebKit; "on" elsewhere)
        support.checkOn = input.value !== "";
    
        // Must access the parent to make an option select properly
        // Support: IE9, IE10
        support.optSelected = opt.selected;
    
        // Make sure that the options inside disabled selects aren't marked as disabled
        // (WebKit marks them as disabled)
        select.disabled = true;
        support.optDisabled = !opt.disabled;
    
        // Check if an input maintains its value after becoming a radio
        // Support: IE9, IE10
        input = document.createElement( "input" );
        input.value = "t";
        input.type = "radio";
        support.radioValue = input.value === "t";
    })();
    
    
    var nodeHook, boolHook,
        attrHandle = jQuery.expr.attrHandle;
    
    jQuery.fn.extend({
        attr: function( name, value ) {
            return access( this, jQuery.attr, name, value, arguments.length > 1 );
        },
    
        removeAttr: function( name ) {
            return this.each(function() {
                jQuery.removeAttr( this, name );
            });
        }
    });
    
    jQuery.extend({
        attr: function( elem, name, value ) {
            var hooks, ret,
                nType = elem.nodeType;
    
            // don't get/set attributes on text, comment and attribute nodes
            if ( !elem || nType === 3 || nType === 8 || nType === 2 ) {
                return;
            }
    
            // Fallback to prop when attributes are not supported
            if ( typeof elem.getAttribute === strundefined ) {
                return jQuery.prop( elem, name, value );
            }
    
            // All attributes are lowercase
            // Grab necessary hook if one is defined
            if ( nType !== 1 || !jQuery.isXMLDoc( elem ) ) {
                name = name.toLowerCase();
                hooks = jQuery.attrHooks[ name ] ||
                    ( jQuery.expr.match.bool.test( name ) ? boolHook : nodeHook );
            }
    
            if ( value !== undefined ) {
    
                if ( value === null ) {
                    jQuery.removeAttr( elem, name );
    
                } else if ( hooks && "set" in hooks && (ret = hooks.set( elem, value, name )) !== undefined ) {
                    return ret;
    
                } else {
                    elem.setAttribute( name, value + "" );
                    return value;
                }
    
            } else if ( hooks && "get" in hooks && (ret = hooks.get( elem, name )) !== null ) {
                return ret;
    
            } else {
                ret = jQuery.find.attr( elem, name );
    
                // Non-existent attributes return null, we normalize to undefined
                return ret == null ?
                    undefined :
                    ret;
            }
        },
    
        removeAttr: function( elem, value ) {
            var name, propName,
                i = 0,
                attrNames = value && value.match( rnotwhite );
    
            if ( attrNames && elem.nodeType === 1 ) {
                while ( (name = attrNames[i++]) ) {
                    propName = jQuery.propFix[ name ] || name;
    
                    // Boolean attributes get special treatment (#10870)
                    if ( jQuery.expr.match.bool.test( name ) ) {
                        // Set corresponding property to false
                        elem[ propName ] = false;
                    }
    
                    elem.removeAttribute( name );
                }
            }
        },
    
        attrHooks: {
            type: {
                set: function( elem, value ) {
                    if ( !support.radioValue && value === "radio" &&
                        jQuery.nodeName( elem, "input" ) ) {
                        // Setting the type on a radio button after the value resets the value in IE6-9
                        // Reset value to default in case type is set after value during creation
                        var val = elem.value;
                        elem.setAttribute( "type", value );
                        if ( val ) {
                            elem.value = val;
                        }
                        return value;
                    }
                }
            }
        }
    });
    
    // Hooks for boolean attributes
    boolHook = {
        set: function( elem, value, name ) {
            if ( value === false ) {
                // Remove boolean attributes when set to false
                jQuery.removeAttr( elem, name );
            } else {
                elem.setAttribute( name, name );
            }
            return name;
        }
    };
    jQuery.each( jQuery.expr.match.bool.source.match( /\w+/g ), function( i, name ) {
        var getter = attrHandle[ name ] || jQuery.find.attr;
    
        attrHandle[ name ] = function( elem, name, isXML ) {
            var ret, handle;
            if ( !isXML ) {
                // Avoid an infinite loop by temporarily removing this function from the getter
                handle = attrHandle[ name ];
                attrHandle[ name ] = ret;
                ret = getter( elem, name, isXML ) != null ?
                    name.toLowerCase() :
                    null;
                attrHandle[ name ] = handle;
            }
            return ret;
        };
    });
    
    
    
    
    var rfocusable = /^(?:input|select|textarea|button)$/i;
    
    jQuery.fn.extend({
        prop: function( name, value ) {
            return access( this, jQuery.prop, name, value, arguments.length > 1 );
        },
    
        removeProp: function( name ) {
            return this.each(function() {
                delete this[ jQuery.propFix[ name ] || name ];
            });
        }
    });
    
    jQuery.extend({
        propFix: {
            "for": "htmlFor",
            "class": "className"
        },
    
        prop: function( elem, name, value ) {
            var ret, hooks, notxml,
                nType = elem.nodeType;
    
            // don't get/set properties on text, comment and attribute nodes
            if ( !elem || nType === 3 || nType === 8 || nType === 2 ) {
                return;
            }
    
            notxml = nType !== 1 || !jQuery.isXMLDoc( elem );
    
            if ( notxml ) {
                // Fix name and attach hooks
                name = jQuery.propFix[ name ] || name;
                hooks = jQuery.propHooks[ name ];
            }
    
            if ( value !== undefined ) {
                return hooks && "set" in hooks && (ret = hooks.set( elem, value, name )) !== undefined ?
                    ret :
                    ( elem[ name ] = value );
    
            } else {
                return hooks && "get" in hooks && (ret = hooks.get( elem, name )) !== null ?
                    ret :
                    elem[ name ];
            }
        },
    
        propHooks: {
            tabIndex: {
                get: function( elem ) {
                    return elem.hasAttribute( "tabindex" ) || rfocusable.test( elem.nodeName ) || elem.href ?
                        elem.tabIndex :
                        -1;
                }
            }
        }
    });
    
    // Support: IE9+
    // Selectedness for an option in an optgroup can be inaccurate
    if ( !support.optSelected ) {
        jQuery.propHooks.selected = {
            get: function( elem ) {
                var parent = elem.parentNode;
                if ( parent && parent.parentNode ) {
                    parent.parentNode.selectedIndex;
                }
                return null;
            }
        };
    }
    
    jQuery.each([
        "tabIndex",
        "readOnly",
        "maxLength",
        "cellSpacing",
        "cellPadding",
        "rowSpan",
        "colSpan",
        "useMap",
        "frameBorder",
        "contentEditable"
    ], function() {
        jQuery.propFix[ this.toLowerCase() ] = this;
    });
    
    
    
    
    var rclass = /[\t\r\n\f]/g;
    
    jQuery.fn.extend({
        addClass: function( value ) {
            var classes, elem, cur, clazz, j, finalValue,
                proceed = typeof value === "string" && value,
                i = 0,
                len = this.length;
    
            if ( jQuery.isFunction( value ) ) {
                return this.each(function( j ) {
                    jQuery( this ).addClass( value.call( this, j, this.className ) );
                });
            }
    
            if ( proceed ) {
                // The disjunction here is for better compressibility (see removeClass)
                classes = ( value || "" ).match( rnotwhite ) || [];
    
                for ( ; i < len; i++ ) {
                    elem = this[ i ];
                    cur = elem.nodeType === 1 && ( elem.className ?
                        ( " " + elem.className + " " ).replace( rclass, " " ) :
                        " "
                    );
    
                    if ( cur ) {
                        j = 0;
                        while ( (clazz = classes[j++]) ) {
                            if ( cur.indexOf( " " + clazz + " " ) < 0 ) {
                                cur += clazz + " ";
                            }
                        }
    
                        // only assign if different to avoid unneeded rendering.
                        finalValue = jQuery.trim( cur );
                        if ( elem.className !== finalValue ) {
                            elem.className = finalValue;
                        }
                    }
                }
            }
    
            return this;
        },
    
        removeClass: function( value ) {
            var classes, elem, cur, clazz, j, finalValue,
                proceed = arguments.length === 0 || typeof value === "string" && value,
                i = 0,
                len = this.length;
    
            if ( jQuery.isFunction( value ) ) {
                return this.each(function( j ) {
                    jQuery( this ).removeClass( value.call( this, j, this.className ) );
                });
            }
            if ( proceed ) {
                classes = ( value || "" ).match( rnotwhite ) || [];
    
                for ( ; i < len; i++ ) {
                    elem = this[ i ];
                    // This expression is here for better compressibility (see addClass)
                    cur = elem.nodeType === 1 && ( elem.className ?
                        ( " " + elem.className + " " ).replace( rclass, " " ) :
                        ""
                    );
    
                    if ( cur ) {
                        j = 0;
                        while ( (clazz = classes[j++]) ) {
                            // Remove *all* instances
                            while ( cur.indexOf( " " + clazz + " " ) >= 0 ) {
                                cur = cur.replace( " " + clazz + " ", " " );
                            }
                        }
    
                        // only assign if different to avoid unneeded rendering.
                        finalValue = value ? jQuery.trim( cur ) : "";
                        if ( elem.className !== finalValue ) {
                            elem.className = finalValue;
                        }
                    }
                }
            }
    
            return this;
        },
    
        toggleClass: function( value, stateVal ) {
            var type = typeof value;
    
            if ( typeof stateVal === "boolean" && type === "string" ) {
                return stateVal ? this.addClass( value ) : this.removeClass( value );
            }
    
            if ( jQuery.isFunction( value ) ) {
                return this.each(function( i ) {
                    jQuery( this ).toggleClass( value.call(this, i, this.className, stateVal), stateVal );
                });
            }
    
            return this.each(function() {
                if ( type === "string" ) {
                    // toggle individual class names
                    var className,
                        i = 0,
                        self = jQuery( this ),
                        classNames = value.match( rnotwhite ) || [];
    
                    while ( (className = classNames[ i++ ]) ) {
                        // check each className given, space separated list
                        if ( self.hasClass( className ) ) {
                            self.removeClass( className );
                        } else {
                            self.addClass( className );
                        }
                    }
    
                // Toggle whole class name
                } else if ( type === strundefined || type === "boolean" ) {
                    if ( this.className ) {
                        // store className if set
                        data_priv.set( this, "__className__", this.className );
                    }
    
                    // If the element has a class name or if we're passed "false",
                    // then remove the whole classname (if there was one, the above saved it).
                    // Otherwise bring back whatever was previously saved (if anything),
                    // falling back to the empty string if nothing was stored.
                    this.className = this.className || value === false ? "" : data_priv.get( this, "__className__" ) || "";
                }
            });
        },
    
        hasClass: function( selector ) {
            var className = " " + selector + " ",
                i = 0,
                l = this.length;
            for ( ; i < l; i++ ) {
                if ( this[i].nodeType === 1 && (" " + this[i].className + " ").replace(rclass, " ").indexOf( className ) >= 0 ) {
                    return true;
                }
            }
    
            return false;
        }
    });
    
    
    
    
    var rreturn = /\r/g;
    
    jQuery.fn.extend({
        val: function( value ) {
            var hooks, ret, isFunction,
                elem = this[0];
    
            if ( !arguments.length ) {
                if ( elem ) {
                    hooks = jQuery.valHooks[ elem.type ] || jQuery.valHooks[ elem.nodeName.toLowerCase() ];
    
                    if ( hooks && "get" in hooks && (ret = hooks.get( elem, "value" )) !== undefined ) {
                        return ret;
                    }
    
                    ret = elem.value;
    
                    return typeof ret === "string" ?
                        // handle most common string cases
                        ret.replace(rreturn, "") :
                        // handle cases where value is null/undef or number
                        ret == null ? "" : ret;
                }
    
                return;
            }
    
            isFunction = jQuery.isFunction( value );
    
            return this.each(function( i ) {
                var val;
    
                if ( this.nodeType !== 1 ) {
                    return;
                }
    
                if ( isFunction ) {
                    val = value.call( this, i, jQuery( this ).val() );
                } else {
                    val = value;
                }
    
                // Treat null/undefined as ""; convert numbers to string
                if ( val == null ) {
                    val = "";
    
                } else if ( typeof val === "number" ) {
                    val += "";
    
                } else if ( jQuery.isArray( val ) ) {
                    val = jQuery.map( val, function( value ) {
                        return value == null ? "" : value + "";
                    });
                }
    
                hooks = jQuery.valHooks[ this.type ] || jQuery.valHooks[ this.nodeName.toLowerCase() ];
    
                // If set returns undefined, fall back to normal setting
                if ( !hooks || !("set" in hooks) || hooks.set( this, val, "value" ) === undefined ) {
                    this.value = val;
                }
            });
        }
    });
    
    jQuery.extend({
        valHooks: {
            option: {
                get: function( elem ) {
                    var val = jQuery.find.attr( elem, "value" );
                    return val != null ?
                        val :
                        // Support: IE10-11+
                        // option.text throws exceptions (#14686, #14858)
                        jQuery.trim( jQuery.text( elem ) );
                }
            },
            select: {
                get: function( elem ) {
                    var value, option,
                        options = elem.options,
                        index = elem.selectedIndex,
                        one = elem.type === "select-one" || index < 0,
                        values = one ? null : [],
                        max = one ? index + 1 : options.length,
                        i = index < 0 ?
                            max :
                            one ? index : 0;
    
                    // Loop through all the selected options
                    for ( ; i < max; i++ ) {
                        option = options[ i ];
    
                        // IE6-9 doesn't update selected after form reset (#2551)
                        if ( ( option.selected || i === index ) &&
                                // Don't return options that are disabled or in a disabled optgroup
                                ( support.optDisabled ? !option.disabled : option.getAttribute( "disabled" ) === null ) &&
                                ( !option.parentNode.disabled || !jQuery.nodeName( option.parentNode, "optgroup" ) ) ) {
    
                            // Get the specific value for the option
                            value = jQuery( option ).val();
    
                            // We don't need an array for one selects
                            if ( one ) {
                                return value;
                            }
    
                            // Multi-Selects return an array
                            values.push( value );
                        }
                    }
    
                    return values;
                },
    
                set: function( elem, value ) {
                    var optionSet, option,
                        options = elem.options,
                        values = jQuery.makeArray( value ),
                        i = options.length;
    
                    while ( i-- ) {
                        option = options[ i ];
                        if ( (option.selected = jQuery.inArray( option.value, values ) >= 0) ) {
                            optionSet = true;
                        }
                    }
    
                    // force browsers to behave consistently when non-matching value is set
                    if ( !optionSet ) {
                        elem.selectedIndex = -1;
                    }
                    return values;
                }
            }
        }
    });
    
    // Radios and checkboxes getter/setter
    jQuery.each([ "radio", "checkbox" ], function() {
        jQuery.valHooks[ this ] = {
            set: function( elem, value ) {
                if ( jQuery.isArray( value ) ) {
                    return ( elem.checked = jQuery.inArray( jQuery(elem).val(), value ) >= 0 );
                }
            }
        };
        if ( !support.checkOn ) {
            jQuery.valHooks[ this ].get = function( elem ) {
                // Support: Webkit
                // "" is returned instead of "on" if a value isn't specified
                return elem.getAttribute("value") === null ? "on" : elem.value;
            };
        }
    });
    
    
    
    
    // Return jQuery for attributes-only inclusion
    
    
    jQuery.each( ("blur focus focusin focusout load resize scroll unload click dblclick " +
        "mousedown mouseup mousemove mouseover mouseout mouseenter mouseleave " +
        "change select submit keydown keypress keyup error contextmenu").split(" "), function( i, name ) {
    
        // Handle event binding
        jQuery.fn[ name ] = function( data, fn ) {
            return arguments.length > 0 ?
                this.on( name, null, data, fn ) :
                this.trigger( name );
        };
    });
    
    jQuery.fn.extend({
        hover: function( fnOver, fnOut ) {
            return this.mouseenter( fnOver ).mouseleave( fnOut || fnOver );
        },
    
        bind: function( types, data, fn ) {
            return this.on( types, null, data, fn );
        },
        unbind: function( types, fn ) {
            return this.off( types, null, fn );
        },
    
        delegate: function( selector, types, data, fn ) {
            return this.on( types, selector, data, fn );
        },
        undelegate: function( selector, types, fn ) {
            // ( namespace ) or ( selector, types [, fn] )
            return arguments.length === 1 ? this.off( selector, "**" ) : this.off( types, selector || "**", fn );
        }
    });
    
    
    var nonce = jQuery.now();
    
    var rquery = (/\?/);
    
    
    
    // Support: Android 2.3
    // Workaround failure to string-cast null input
    jQuery.parseJSON = function( data ) {
        return JSON.parse( data + "" );
    };
    
    
    // Cross-browser xml parsing
    jQuery.parseXML = function( data ) {
        var xml, tmp;
        if ( !data || typeof data !== "string" ) {
            return null;
        }
    
        // Support: IE9
        try {
            tmp = new DOMParser();
            xml = tmp.parseFromString( data, "text/xml" );
        } catch ( e ) {
            xml = undefined;
        }
    
        if ( !xml || xml.getElementsByTagName( "parsererror" ).length ) {
            jQuery.error( "Invalid XML: " + data );
        }
        return xml;
    };
    
    
    var
        // Document location
        ajaxLocParts,
        ajaxLocation,
    
        rhash = /#.*$/,
        rts = /([?&])_=[^&]*/,
        rheaders = /^(.*?):[ \t]*([^\r\n]*)$/mg,
        // #7653, #8125, #8152: local protocol detection
        rlocalProtocol = /^(?:about|app|app-storage|.+-extension|file|res|widget):$/,
        rnoContent = /^(?:GET|HEAD)$/,
        rprotocol = /^\/\//,
        rurl = /^([\w.+-]+:)(?:\/\/(?:[^\/?#]*@|)([^\/?#:]*)(?::(\d+)|)|)/,
    
        /* Prefilters
         * 1) They are useful to introduce custom dataTypes (see ajax/jsonp.js for an example)
         * 2) These are called:
         *    - BEFORE asking for a transport
         *    - AFTER param serialization (s.data is a string if s.processData is true)
         * 3) key is the dataType
         * 4) the catchall symbol "*" can be used
         * 5) execution will start with transport dataType and THEN continue down to "*" if needed
         */
        prefilters = {},
    
        /* Transports bindings
         * 1) key is the dataType
         * 2) the catchall symbol "*" can be used
         * 3) selection will start with transport dataType and THEN go to "*" if needed
         */
        transports = {},
    
        // Avoid comment-prolog char sequence (#10098); must appease lint and evade compression
        allTypes = "*/".concat("*");
    
    // #8138, IE may throw an exception when accessing
    // a field from window.location if document.domain has been set
    try {
        ajaxLocation = location.href;
    } catch( e ) {
        // Use the href attribute of an A element
        // since IE will modify it given document.location
        ajaxLocation = document.createElement( "a" );
        ajaxLocation.href = "";
        ajaxLocation = ajaxLocation.href;
    }
    
    // Segment location into parts
    ajaxLocParts = rurl.exec( ajaxLocation.toLowerCase() ) || [];
    
    // Base "constructor" for jQuery.ajaxPrefilter and jQuery.ajaxTransport
    function addToPrefiltersOrTransports( structure ) {
    
        // dataTypeExpression is optional and defaults to "*"
        return function( dataTypeExpression, func ) {
    
            if ( typeof dataTypeExpression !== "string" ) {
                func = dataTypeExpression;
                dataTypeExpression = "*";
            }
    
            var dataType,
                i = 0,
                dataTypes = dataTypeExpression.toLowerCase().match( rnotwhite ) || [];
    
            if ( jQuery.isFunction( func ) ) {
                // For each dataType in the dataTypeExpression
                while ( (dataType = dataTypes[i++]) ) {
                    // Prepend if requested
                    if ( dataType[0] === "+" ) {
                        dataType = dataType.slice( 1 ) || "*";
                        (structure[ dataType ] = structure[ dataType ] || []).unshift( func );
    
                    // Otherwise append
                    } else {
                        (structure[ dataType ] = structure[ dataType ] || []).push( func );
                    }
                }
            }
        };
    }
    
    // Base inspection function for prefilters and transports
    function inspectPrefiltersOrTransports( structure, options, originalOptions, jqXHR ) {
    
        var inspected = {},
            seekingTransport = ( structure === transports );
    
        function inspect( dataType ) {
            var selected;
            inspected[ dataType ] = true;
            jQuery.each( structure[ dataType ] || [], function( _, prefilterOrFactory ) {
                var dataTypeOrTransport = prefilterOrFactory( options, originalOptions, jqXHR );
                if ( typeof dataTypeOrTransport === "string" && !seekingTransport && !inspected[ dataTypeOrTransport ] ) {
                    options.dataTypes.unshift( dataTypeOrTransport );
                    inspect( dataTypeOrTransport );
                    return false;
                } else if ( seekingTransport ) {
                    return !( selected = dataTypeOrTransport );
                }
            });
            return selected;
        }
    
        return inspect( options.dataTypes[ 0 ] ) || !inspected[ "*" ] && inspect( "*" );
    }
    
    // A special extend for ajax options
    // that takes "flat" options (not to be deep extended)
    // Fixes #9887
    function ajaxExtend( target, src ) {
        var key, deep,
            flatOptions = jQuery.ajaxSettings.flatOptions || {};
    
        for ( key in src ) {
            if ( src[ key ] !== undefined ) {
                ( flatOptions[ key ] ? target : ( deep || (deep = {}) ) )[ key ] = src[ key ];
            }
        }
        if ( deep ) {
            jQuery.extend( true, target, deep );
        }
    
        return target;
    }
    
    /* Handles responses to an ajax request:
     * - finds the right dataType (mediates between content-type and expected dataType)
     * - returns the corresponding response
     */
    function ajaxHandleResponses( s, jqXHR, responses ) {
    
        var ct, type, finalDataType, firstDataType,
            contents = s.contents,
            dataTypes = s.dataTypes;
    
        // Remove auto dataType and get content-type in the process
        while ( dataTypes[ 0 ] === "*" ) {
            dataTypes.shift();
            if ( ct === undefined ) {
                ct = s.mimeType || jqXHR.getResponseHeader("Content-Type");
            }
        }
    
        // Check if we're dealing with a known content-type
        if ( ct ) {
            for ( type in contents ) {
                if ( contents[ type ] && contents[ type ].test( ct ) ) {
                    dataTypes.unshift( type );
                    break;
                }
            }
        }
    
        // Check to see if we have a response for the expected dataType
        if ( dataTypes[ 0 ] in responses ) {
            finalDataType = dataTypes[ 0 ];
        } else {
            // Try convertible dataTypes
            for ( type in responses ) {
                if ( !dataTypes[ 0 ] || s.converters[ type + " " + dataTypes[0] ] ) {
                    finalDataType = type;
                    break;
                }
                if ( !firstDataType ) {
                    firstDataType = type;
                }
            }
            // Or just use first one
            finalDataType = finalDataType || firstDataType;
        }
    
        // If we found a dataType
        // We add the dataType to the list if needed
        // and return the corresponding response
        if ( finalDataType ) {
            if ( finalDataType !== dataTypes[ 0 ] ) {
                dataTypes.unshift( finalDataType );
            }
            return responses[ finalDataType ];
        }
    }
    
    /* Chain conversions given the request and the original response
     * Also sets the responseXXX fields on the jqXHR instance
     */
    function ajaxConvert( s, response, jqXHR, isSuccess ) {
        var conv2, current, conv, tmp, prev,
            converters = {},
            // Work with a copy of dataTypes in case we need to modify it for conversion
            dataTypes = s.dataTypes.slice();
    
        // Create converters map with lowercased keys
        if ( dataTypes[ 1 ] ) {
            for ( conv in s.converters ) {
                converters[ conv.toLowerCase() ] = s.converters[ conv ];
            }
        }
    
        current = dataTypes.shift();
    
        // Convert to each sequential dataType
        while ( current ) {
    
            if ( s.responseFields[ current ] ) {
                jqXHR[ s.responseFields[ current ] ] = response;
            }
    
            // Apply the dataFilter if provided
            if ( !prev && isSuccess && s.dataFilter ) {
                response = s.dataFilter( response, s.dataType );
            }
    
            prev = current;
            current = dataTypes.shift();
    
            if ( current ) {
    
            // There's only work to do if current dataType is non-auto
                if ( current === "*" ) {
    
                    current = prev;
    
                // Convert response if prev dataType is non-auto and differs from current
                } else if ( prev !== "*" && prev !== current ) {
    
                    // Seek a direct converter
                    conv = converters[ prev + " " + current ] || converters[ "* " + current ];
    
                    // If none found, seek a pair
                    if ( !conv ) {
                        for ( conv2 in converters ) {
    
                            // If conv2 outputs current
                            tmp = conv2.split( " " );
                            if ( tmp[ 1 ] === current ) {
    
                                // If prev can be converted to accepted input
                                conv = converters[ prev + " " + tmp[ 0 ] ] ||
                                    converters[ "* " + tmp[ 0 ] ];
                                if ( conv ) {
                                    // Condense equivalence converters
                                    if ( conv === true ) {
                                        conv = converters[ conv2 ];
    
                                    // Otherwise, insert the intermediate dataType
                                    } else if ( converters[ conv2 ] !== true ) {
                                        current = tmp[ 0 ];
                                        dataTypes.unshift( tmp[ 1 ] );
                                    }
                                    break;
                                }
                            }
                        }
                    }
    
                    // Apply converter (if not an equivalence)
                    if ( conv !== true ) {
    
                        // Unless errors are allowed to bubble, catch and return them
                        if ( conv && s[ "throws" ] ) {
                            response = conv( response );
                        } else {
                            try {
                                response = conv( response );
                            } catch ( e ) {
                                return { state: "parsererror", error: conv ? e : "No conversion from " + prev + " to " + current };
                            }
                        }
                    }
                }
            }
        }
    
        return { state: "success", data: response };
    }
    
    jQuery.extend({
    
        // Counter for holding the number of active queries
        active: 0,
    
        // Last-Modified header cache for next request
        lastModified: {},
        etag: {},
    
        ajaxSettings: {
            url: ajaxLocation,
            type: "GET",
            isLocal: rlocalProtocol.test( ajaxLocParts[ 1 ] ),
            global: true,
            processData: true,
            async: true,
            contentType: "application/x-www-form-urlencoded; charset=UTF-8",
            /*
            timeout: 0,
            data: null,
            dataType: null,
            username: null,
            password: null,
            cache: null,
            throws: false,
            traditional: false,
            headers: {},
            */
    
            accepts: {
                "*": allTypes,
                text: "text/plain",
                html: "text/html",
                xml: "application/xml, text/xml",
                json: "application/json, text/javascript"
            },
    
            contents: {
                xml: /xml/,
                html: /html/,
                json: /json/
            },
    
            responseFields: {
                xml: "responseXML",
                text: "responseText",
                json: "responseJSON"
            },
    
            // Data converters
            // Keys separate source (or catchall "*") and destination types with a single space
            converters: {
    
                // Convert anything to text
                "* text": String,
    
                // Text to html (true = no transformation)
                "text html": true,
    
                // Evaluate text as a json expression
                "text json": jQuery.parseJSON,
    
                // Parse text as xml
                "text xml": jQuery.parseXML
            },
    
            // For options that shouldn't be deep extended:
            // you can add your own custom options here if
            // and when you create one that shouldn't be
            // deep extended (see ajaxExtend)
            flatOptions: {
                url: true,
                context: true
            }
        },
    
        // Creates a full fledged settings object into target
        // with both ajaxSettings and settings fields.
        // If target is omitted, writes into ajaxSettings.
        ajaxSetup: function( target, settings ) {
            return settings ?
    
                // Building a settings object
                ajaxExtend( ajaxExtend( target, jQuery.ajaxSettings ), settings ) :
    
                // Extending ajaxSettings
                ajaxExtend( jQuery.ajaxSettings, target );
        },
    
        ajaxPrefilter: addToPrefiltersOrTransports( prefilters ),
        ajaxTransport: addToPrefiltersOrTransports( transports ),
    
        // Main method
        ajax: function( url, options ) {
    
            // If url is an object, simulate pre-1.5 signature
            if ( typeof url === "object" ) {
                options = url;
                url = undefined;
            }
    
            // Force options to be an object
            options = options || {};
    
            var transport,
                // URL without anti-cache param
                cacheURL,
                // Response headers
                responseHeadersString,
                responseHeaders,
                // timeout handle
                timeoutTimer,
                // Cross-domain detection vars
                parts,
                // To know if global events are to be dispatched
                fireGlobals,
                // Loop variable
                i,
                // Create the final options object
                s = jQuery.ajaxSetup( {}, options ),
                // Callbacks context
                callbackContext = s.context || s,
                // Context for global events is callbackContext if it is a DOM node or jQuery collection
                globalEventContext = s.context && ( callbackContext.nodeType || callbackContext.jquery ) ?
                    jQuery( callbackContext ) :
                    jQuery.event,
                // Deferreds
                deferred = jQuery.Deferred(),
                completeDeferred = jQuery.Callbacks("once memory"),
                // Status-dependent callbacks
                statusCode = s.statusCode || {},
                // Headers (they are sent all at once)
                requestHeaders = {},
                requestHeadersNames = {},
                // The jqXHR state
                state = 0,
                // Default abort message
                strAbort = "canceled",
                // Fake xhr
                jqXHR = {
                    readyState: 0,
    
                    // Builds headers hashtable if needed
                    getResponseHeader: function( key ) {
                        var match;
                        if ( state === 2 ) {
                            if ( !responseHeaders ) {
                                responseHeaders = {};
                                while ( (match = rheaders.exec( responseHeadersString )) ) {
                                    responseHeaders[ match[1].toLowerCase() ] = match[ 2 ];
                                }
                            }
                            match = responseHeaders[ key.toLowerCase() ];
                        }
                        return match == null ? null : match;
                    },
    
                    // Raw string
                    getAllResponseHeaders: function() {
                        return state === 2 ? responseHeadersString : null;
                    },
    
                    // Caches the header
                    setRequestHeader: function( name, value ) {
                        var lname = name.toLowerCase();
                        if ( !state ) {
                            name = requestHeadersNames[ lname ] = requestHeadersNames[ lname ] || name;
                            requestHeaders[ name ] = value;
                        }
                        return this;
                    },
    
                    // Overrides response content-type header
                    overrideMimeType: function( type ) {
                        if ( !state ) {
                            s.mimeType = type;
                        }
                        return this;
                    },
    
                    // Status-dependent callbacks
                    statusCode: function( map ) {
                        var code;
                        if ( map ) {
                            if ( state < 2 ) {
                                for ( code in map ) {
                                    // Lazy-add the new callback in a way that preserves old ones
                                    statusCode[ code ] = [ statusCode[ code ], map[ code ] ];
                                }
                            } else {
                                // Execute the appropriate callbacks
                                jqXHR.always( map[ jqXHR.status ] );
                            }
                        }
                        return this;
                    },
    
                    // Cancel the request
                    abort: function( statusText ) {
                        var finalText = statusText || strAbort;
                        if ( transport ) {
                            transport.abort( finalText );
                        }
                        done( 0, finalText );
                        return this;
                    }
                };
    
            // Attach deferreds
            deferred.promise( jqXHR ).complete = completeDeferred.add;
            jqXHR.success = jqXHR.done;
            jqXHR.error = jqXHR.fail;
    
            // Remove hash character (#7531: and string promotion)
            // Add protocol if not provided (prefilters might expect it)
            // Handle falsy url in the settings object (#10093: consistency with old signature)
            // We also use the url parameter if available
            s.url = ( ( url || s.url || ajaxLocation ) + "" ).replace( rhash, "" )
                .replace( rprotocol, ajaxLocParts[ 1 ] + "//" );
    
            // Alias method option to type as per ticket #12004
            s.type = options.method || options.type || s.method || s.type;
    
            // Extract dataTypes list
            s.dataTypes = jQuery.trim( s.dataType || "*" ).toLowerCase().match( rnotwhite ) || [ "" ];
    
            // A cross-domain request is in order when we have a protocol:host:port mismatch
            if ( s.crossDomain == null ) {
                parts = rurl.exec( s.url.toLowerCase() );
                s.crossDomain = !!( parts &&
                    ( parts[ 1 ] !== ajaxLocParts[ 1 ] || parts[ 2 ] !== ajaxLocParts[ 2 ] ||
                        ( parts[ 3 ] || ( parts[ 1 ] === "http:" ? "80" : "443" ) ) !==
                            ( ajaxLocParts[ 3 ] || ( ajaxLocParts[ 1 ] === "http:" ? "80" : "443" ) ) )
                );
            }
    
            // Convert data if not already a string
            if ( s.data && s.processData && typeof s.data !== "string" ) {
                s.data = jQuery.param( s.data, s.traditional );
            }
    
            // Apply prefilters
            inspectPrefiltersOrTransports( prefilters, s, options, jqXHR );
    
            // If request was aborted inside a prefilter, stop there
            if ( state === 2 ) {
                return jqXHR;
            }
    
            // We can fire global events as of now if asked to
            fireGlobals = s.global;
    
            // Watch for a new set of requests
            if ( fireGlobals && jQuery.active++ === 0 ) {
                jQuery.event.trigger("ajaxStart");
            }
    
            // Uppercase the type
            s.type = s.type.toUpperCase();
    
            // Determine if request has content
            s.hasContent = !rnoContent.test( s.type );
    
            // Save the URL in case we're toying with the If-Modified-Since
            // and/or If-None-Match header later on
            cacheURL = s.url;
    
            // More options handling for requests with no content
            if ( !s.hasContent ) {
    
                // If data is available, append data to url
                if ( s.data ) {
                    cacheURL = ( s.url += ( rquery.test( cacheURL ) ? "&" : "?" ) + s.data );
                    // #9682: remove data so that it's not used in an eventual retry
                    delete s.data;
                }
    
                // Add anti-cache in url if needed
                if ( s.cache === false ) {
                    s.url = rts.test( cacheURL ) ?
    
                        // If there is already a '_' parameter, set its value
                        cacheURL.replace( rts, "$1_=" + nonce++ ) :
    
                        // Otherwise add one to the end
                        cacheURL + ( rquery.test( cacheURL ) ? "&" : "?" ) + "_=" + nonce++;
                }
            }
    
            // Set the If-Modified-Since and/or If-None-Match header, if in ifModified mode.
            if ( s.ifModified ) {
                if ( jQuery.lastModified[ cacheURL ] ) {
                    jqXHR.setRequestHeader( "If-Modified-Since", jQuery.lastModified[ cacheURL ] );
                }
                if ( jQuery.etag[ cacheURL ] ) {
                    jqXHR.setRequestHeader( "If-None-Match", jQuery.etag[ cacheURL ] );
                }
            }
    
            // Set the correct header, if data is being sent
            if ( s.data && s.hasContent && s.contentType !== false || options.contentType ) {
                jqXHR.setRequestHeader( "Content-Type", s.contentType );
            }
    
            // Set the Accepts header for the server, depending on the dataType
            jqXHR.setRequestHeader(
                "Accept",
                s.dataTypes[ 0 ] && s.accepts[ s.dataTypes[0] ] ?
                    s.accepts[ s.dataTypes[0] ] + ( s.dataTypes[ 0 ] !== "*" ? ", " + allTypes + "; q=0.01" : "" ) :
                    s.accepts[ "*" ]
            );
    
            // Check for headers option
            for ( i in s.headers ) {
                jqXHR.setRequestHeader( i, s.headers[ i ] );
            }
    
            // Allow custom headers/mimetypes and early abort
            if ( s.beforeSend && ( s.beforeSend.call( callbackContext, jqXHR, s ) === false || state === 2 ) ) {
                // Abort if not done already and return
                return jqXHR.abort();
            }
    
            // aborting is no longer a cancellation
            strAbort = "abort";
    
            // Install callbacks on deferreds
            for ( i in { success: 1, error: 1, complete: 1 } ) {
                jqXHR[ i ]( s[ i ] );
            }
    
            // Get transport
            transport = inspectPrefiltersOrTransports( transports, s, options, jqXHR );
    
            // If no transport, we auto-abort
            if ( !transport ) {
                done( -1, "No Transport" );
            } else {
                jqXHR.readyState = 1;
    
                // Send global event
                if ( fireGlobals ) {
                    globalEventContext.trigger( "ajaxSend", [ jqXHR, s ] );
                }
                // Timeout
                if ( s.async && s.timeout > 0 ) {
                    timeoutTimer = setTimeout(function() {
                        jqXHR.abort("timeout");
                    }, s.timeout );
                }
    
                try {
                    state = 1;
                    transport.send( requestHeaders, done );
                } catch ( e ) {
                    // Propagate exception as error if not done
                    if ( state < 2 ) {
                        done( -1, e );
                    // Simply rethrow otherwise
                    } else {
                        throw e;
                    }
                }
            }
    
            // Callback for when everything is done
            function done( status, nativeStatusText, responses, headers ) {
                var isSuccess, success, error, response, modified,
                    statusText = nativeStatusText;
    
                // Called once
                if ( state === 2 ) {
                    return;
                }
    
                // State is "done" now
                state = 2;
    
                // Clear timeout if it exists
                if ( timeoutTimer ) {
                    clearTimeout( timeoutTimer );
                }
    
                // Dereference transport for early garbage collection
                // (no matter how long the jqXHR object will be used)
                transport = undefined;
    
                // Cache response headers
                responseHeadersString = headers || "";
    
                // Set readyState
                jqXHR.readyState = status > 0 ? 4 : 0;
    
                // Determine if successful
                isSuccess = status >= 200 && status < 300 || status === 304;
    
                // Get response data
                if ( responses ) {
                    response = ajaxHandleResponses( s, jqXHR, responses );
                }
    
                // Convert no matter what (that way responseXXX fields are always set)
                response = ajaxConvert( s, response, jqXHR, isSuccess );
    
                // If successful, handle type chaining
                if ( isSuccess ) {
    
                    // Set the If-Modified-Since and/or If-None-Match header, if in ifModified mode.
                    if ( s.ifModified ) {
                        modified = jqXHR.getResponseHeader("Last-Modified");
                        if ( modified ) {
                            jQuery.lastModified[ cacheURL ] = modified;
                        }
                        modified = jqXHR.getResponseHeader("etag");
                        if ( modified ) {
                            jQuery.etag[ cacheURL ] = modified;
                        }
                    }
    
                    // if no content
                    if ( status === 204 || s.type === "HEAD" ) {
                        statusText = "nocontent";
    
                    // if not modified
                    } else if ( status === 304 ) {
                        statusText = "notmodified";
    
                    // If we have data, let's convert it
                    } else {
                        statusText = response.state;
                        success = response.data;
                        error = response.error;
                        isSuccess = !error;
                    }
                } else {
                    // We extract error from statusText
                    // then normalize statusText and status for non-aborts
                    error = statusText;
                    if ( status || !statusText ) {
                        statusText = "error";
                        if ( status < 0 ) {
                            status = 0;
                        }
                    }
                }
    
                // Set data for the fake xhr object
                jqXHR.status = status;
                jqXHR.statusText = ( nativeStatusText || statusText ) + "";
    
                // Success/Error
                if ( isSuccess ) {
                    deferred.resolveWith( callbackContext, [ success, statusText, jqXHR ] );
                } else {
                    deferred.rejectWith( callbackContext, [ jqXHR, statusText, error ] );
                }
    
                // Status-dependent callbacks
                jqXHR.statusCode( statusCode );
                statusCode = undefined;
    
                if ( fireGlobals ) {
                    globalEventContext.trigger( isSuccess ? "ajaxSuccess" : "ajaxError",
                        [ jqXHR, s, isSuccess ? success : error ] );
                }
    
                // Complete
                completeDeferred.fireWith( callbackContext, [ jqXHR, statusText ] );
    
                if ( fireGlobals ) {
                    globalEventContext.trigger( "ajaxComplete", [ jqXHR, s ] );
                    // Handle the global AJAX counter
                    if ( !( --jQuery.active ) ) {
                        jQuery.event.trigger("ajaxStop");
                    }
                }
            }
    
            return jqXHR;
        },
    
        getJSON: function( url, data, callback ) {
            return jQuery.get( url, data, callback, "json" );
        },
    
        getScript: function( url, callback ) {
            return jQuery.get( url, undefined, callback, "script" );
        }
    });
    
    jQuery.each( [ "get", "post" ], function( i, method ) {
        jQuery[ method ] = function( url, data, callback, type ) {
            // shift arguments if data argument was omitted
            if ( jQuery.isFunction( data ) ) {
                type = type || callback;
                callback = data;
                data = undefined;
            }
    
            return jQuery.ajax({
                url: url,
                type: method,
                dataType: type,
                data: data,
                success: callback
            });
        };
    });
    
    // Attach a bunch of functions for handling common AJAX events
    jQuery.each( [ "ajaxStart", "ajaxStop", "ajaxComplete", "ajaxError", "ajaxSuccess", "ajaxSend" ], function( i, type ) {
        jQuery.fn[ type ] = function( fn ) {
            return this.on( type, fn );
        };
    });
    
    
    jQuery._evalUrl = function( url ) {
        return jQuery.ajax({
            url: url,
            type: "GET",
            dataType: "script",
            async: false,
            global: false,
            "throws": true
        });
    };
    
    
    jQuery.fn.extend({
        wrapAll: function( html ) {
            var wrap;
    
            if ( jQuery.isFunction( html ) ) {
                return this.each(function( i ) {
                    jQuery( this ).wrapAll( html.call(this, i) );
                });
            }
    
            if ( this[ 0 ] ) {
    
                // The elements to wrap the target around
                wrap = jQuery( html, this[ 0 ].ownerDocument ).eq( 0 ).clone( true );
    
                if ( this[ 0 ].parentNode ) {
                    wrap.insertBefore( this[ 0 ] );
                }
    
                wrap.map(function() {
                    var elem = this;
    
                    while ( elem.firstElementChild ) {
                        elem = elem.firstElementChild;
                    }
    
                    return elem;
                }).append( this );
            }
    
            return this;
        },
    
        wrapInner: function( html ) {
            if ( jQuery.isFunction( html ) ) {
                return this.each(function( i ) {
                    jQuery( this ).wrapInner( html.call(this, i) );
                });
            }
    
            return this.each(function() {
                var self = jQuery( this ),
                    contents = self.contents();
    
                if ( contents.length ) {
                    contents.wrapAll( html );
    
                } else {
                    self.append( html );
                }
            });
        },
    
        wrap: function( html ) {
            var isFunction = jQuery.isFunction( html );
    
            return this.each(function( i ) {
                jQuery( this ).wrapAll( isFunction ? html.call(this, i) : html );
            });
        },
    
        unwrap: function() {
            return this.parent().each(function() {
                if ( !jQuery.nodeName( this, "body" ) ) {
                    jQuery( this ).replaceWith( this.childNodes );
                }
            }).end();
        }
    });
    
    
    jQuery.expr.filters.hidden = function( elem ) {
        // Support: Opera <= 12.12
        // Opera reports offsetWidths and offsetHeights less than zero on some elements
        return elem.offsetWidth <= 0 && elem.offsetHeight <= 0;
    };
    jQuery.expr.filters.visible = function( elem ) {
        return !jQuery.expr.filters.hidden( elem );
    };
    
    
    
    
    var r20 = /%20/g,
        rbracket = /\[\]$/,
        rCRLF = /\r?\n/g,
        rsubmitterTypes = /^(?:submit|button|image|reset|file)$/i,
        rsubmittable = /^(?:input|select|textarea|keygen)/i;
    
    function buildParams( prefix, obj, traditional, add ) {
        var name;
    
        if ( jQuery.isArray( obj ) ) {
            // Serialize array item.
            jQuery.each( obj, function( i, v ) {
                if ( traditional || rbracket.test( prefix ) ) {
                    // Treat each array item as a scalar.
                    add( prefix, v );
    
                } else {
                    // Item is non-scalar (array or object), encode its numeric index.
                    buildParams( prefix + "[" + ( typeof v === "object" ? i : "" ) + "]", v, traditional, add );
                }
            });
    
        } else if ( !traditional && jQuery.type( obj ) === "object" ) {
            // Serialize object item.
            for ( name in obj ) {
                buildParams( prefix + "[" + name + "]", obj[ name ], traditional, add );
            }
    
        } else {
            // Serialize scalar item.
            add( prefix, obj );
        }
    }
    
    // Serialize an array of form elements or a set of
    // key/values into a query string
    jQuery.param = function( a, traditional ) {
        var prefix,
            s = [],
            add = function( key, value ) {
                // If value is a function, invoke it and return its value
                value = jQuery.isFunction( value ) ? value() : ( value == null ? "" : value );
                s[ s.length ] = encodeURIComponent( key ) + "=" + encodeURIComponent( value );
            };
    
        // Set traditional to true for jQuery <= 1.3.2 behavior.
        if ( traditional === undefined ) {
            traditional = jQuery.ajaxSettings && jQuery.ajaxSettings.traditional;
        }
    
        // If an array was passed in, assume that it is an array of form elements.
        if ( jQuery.isArray( a ) || ( a.jquery && !jQuery.isPlainObject( a ) ) ) {
            // Serialize the form elements
            jQuery.each( a, function() {
                add( this.name, this.value );
            });
    
        } else {
            // If traditional, encode the "old" way (the way 1.3.2 or older
            // did it), otherwise encode params recursively.
            for ( prefix in a ) {
                buildParams( prefix, a[ prefix ], traditional, add );
            }
        }
    
        // Return the resulting serialization
        return s.join( "&" ).replace( r20, "+" );
    };
    
    jQuery.fn.extend({
        serialize: function() {
            return jQuery.param( this.serializeArray() );
        },
        serializeArray: function() {
            return this.map(function() {
                // Can add propHook for "elements" to filter or add form elements
                var elements = jQuery.prop( this, "elements" );
                return elements ? jQuery.makeArray( elements ) : this;
            })
            .filter(function() {
                var type = this.type;
    
                // Use .is( ":disabled" ) so that fieldset[disabled] works
                return this.name && !jQuery( this ).is( ":disabled" ) &&
                    rsubmittable.test( this.nodeName ) && !rsubmitterTypes.test( type ) &&
                    ( this.checked || !rcheckableType.test( type ) );
            })
            .map(function( i, elem ) {
                var val = jQuery( this ).val();
    
                return val == null ?
                    null :
                    jQuery.isArray( val ) ?
                        jQuery.map( val, function( val ) {
                            return { name: elem.name, value: val.replace( rCRLF, "\r\n" ) };
                        }) :
                        { name: elem.name, value: val.replace( rCRLF, "\r\n" ) };
            }).get();
        }
    });
    
    
    jQuery.ajaxSettings.xhr = function() {
        try {
            return new XMLHttpRequest();
        } catch( e ) {}
    };
    
    var xhrId = 0,
        xhrCallbacks = {},
        xhrSuccessStatus = {
            // file protocol always yields status code 0, assume 200
            0: 200,
            // Support: IE9
            // #1450: sometimes IE returns 1223 when it should be 204
            1223: 204
        },
        xhrSupported = jQuery.ajaxSettings.xhr();
    
    // Support: IE9
    // Open requests must be manually aborted on unload (#5280)
    if ( window.ActiveXObject ) {
        jQuery( window ).on( "unload", function() {
            for ( var key in xhrCallbacks ) {
                xhrCallbacks[ key ]();
            }
        });
    }
    
    support.cors = !!xhrSupported && ( "withCredentials" in xhrSupported );
    support.ajax = xhrSupported = !!xhrSupported;
    
    jQuery.ajaxTransport(function( options ) {
        var callback;
    
        // Cross domain only allowed if supported through XMLHttpRequest
        if ( support.cors || xhrSupported && !options.crossDomain ) {
            return {
                send: function( headers, complete ) {
                    var i,
                        xhr = options.xhr(),
                        id = ++xhrId;
    
                    xhr.open( options.type, options.url, options.async, options.username, options.password );
    
                    // Apply custom fields if provided
                    if ( options.xhrFields ) {
                        for ( i in options.xhrFields ) {
                            xhr[ i ] = options.xhrFields[ i ];
                        }
                    }
    
                    // Override mime type if needed
                    if ( options.mimeType && xhr.overrideMimeType ) {
                        xhr.overrideMimeType( options.mimeType );
                    }
    
                    // X-Requested-With header
                    // For cross-domain requests, seeing as conditions for a preflight are
                    // akin to a jigsaw puzzle, we simply never set it to be sure.
                    // (it can always be set on a per-request basis or even using ajaxSetup)
                    // For same-domain requests, won't change header if already provided.
                    if ( !options.crossDomain && !headers["X-Requested-With"] ) {
                        headers["X-Requested-With"] = "XMLHttpRequest";
                    }
    
                    // Set headers
                    for ( i in headers ) {
                        xhr.setRequestHeader( i, headers[ i ] );
                    }
    
                    // Callback
                    callback = function( type ) {
                        return function() {
                            if ( callback ) {
                                delete xhrCallbacks[ id ];
                                callback = xhr.onload = xhr.onerror = null;
    
                                if ( type === "abort" ) {
                                    xhr.abort();
                                } else if ( type === "error" ) {
                                    complete(
                                        // file: protocol always yields status 0; see #8605, #14207
                                        xhr.status,
                                        xhr.statusText
                                    );
                                } else {
                                    complete(
                                        xhrSuccessStatus[ xhr.status ] || xhr.status,
                                        xhr.statusText,
                                        // Support: IE9
                                        // Accessing binary-data responseText throws an exception
                                        // (#11426)
                                        typeof xhr.responseText === "string" ? {
                                            text: xhr.responseText
                                        } : undefined,
                                        xhr.getAllResponseHeaders()
                                    );
                                }
                            }
                        };
                    };
    
                    // Listen to events
                    xhr.onload = callback();
                    xhr.onerror = callback("error");
    
                    // Create the abort callback
                    callback = xhrCallbacks[ id ] = callback("abort");
    
                    try {
                        // Do send the request (this may raise an exception)
                        xhr.send( options.hasContent && options.data || null );
                    } catch ( e ) {
                        // #14683: Only rethrow if this hasn't been notified as an error yet
                        if ( callback ) {
                            throw e;
                        }
                    }
                },
    
                abort: function() {
                    if ( callback ) {
                        callback();
                    }
                }
            };
        }
    });
    
    
    
    
    // Install script dataType
    jQuery.ajaxSetup({
        accepts: {
            script: "text/javascript, application/javascript, application/ecmascript, application/x-ecmascript"
        },
        contents: {
            script: /(?:java|ecma)script/
        },
        converters: {
            "text script": function( text ) {
                jQuery.globalEval( text );
                return text;
            }
        }
    });
    
    // Handle cache's special case and crossDomain
    jQuery.ajaxPrefilter( "script", function( s ) {
        if ( s.cache === undefined ) {
            s.cache = false;
        }
        if ( s.crossDomain ) {
            s.type = "GET";
        }
    });
    
    // Bind script tag hack transport
    jQuery.ajaxTransport( "script", function( s ) {
        // This transport only deals with cross domain requests
        if ( s.crossDomain ) {
            var script, callback;
            return {
                send: function( _, complete ) {
                    script = jQuery("<script>").prop({
                        async: true,
                        charset: s.scriptCharset,
                        src: s.url
                    }).on(
                        "load error",
                        callback = function( evt ) {
                            script.remove();
                            callback = null;
                            if ( evt ) {
                                complete( evt.type === "error" ? 404 : 200, evt.type );
                            }
                        }
                    );
                    document.head.appendChild( script[ 0 ] );
                },
                abort: function() {
                    if ( callback ) {
                        callback();
                    }
                }
            };
        }
    });
    
    
    
    
    var oldCallbacks = [],
        rjsonp = /(=)\?(?=&|$)|\?\?/;
    
    // Default jsonp settings
    jQuery.ajaxSetup({
        jsonp: "callback",
        jsonpCallback: function() {
            var callback = oldCallbacks.pop() || ( jQuery.expando + "_" + ( nonce++ ) );
            this[ callback ] = true;
            return callback;
        }
    });
    
    // Detect, normalize options and install callbacks for jsonp requests
    jQuery.ajaxPrefilter( "json jsonp", function( s, originalSettings, jqXHR ) {
    
        var callbackName, overwritten, responseContainer,
            jsonProp = s.jsonp !== false && ( rjsonp.test( s.url ) ?
                "url" :
                typeof s.data === "string" && !( s.contentType || "" ).indexOf("application/x-www-form-urlencoded") && rjsonp.test( s.data ) && "data"
            );
    
        // Handle iff the expected data type is "jsonp" or we have a parameter to set
        if ( jsonProp || s.dataTypes[ 0 ] === "jsonp" ) {
    
            // Get callback name, remembering preexisting value associated with it
            callbackName = s.jsonpCallback = jQuery.isFunction( s.jsonpCallback ) ?
                s.jsonpCallback() :
                s.jsonpCallback;
    
            // Insert callback into url or form data
            if ( jsonProp ) {
                s[ jsonProp ] = s[ jsonProp ].replace( rjsonp, "$1" + callbackName );
            } else if ( s.jsonp !== false ) {
                s.url += ( rquery.test( s.url ) ? "&" : "?" ) + s.jsonp + "=" + callbackName;
            }
    
            // Use data converter to retrieve json after script execution
            s.converters["script json"] = function() {
                if ( !responseContainer ) {
                    jQuery.error( callbackName + " was not called" );
                }
                return responseContainer[ 0 ];
            };
    
            // force json dataType
            s.dataTypes[ 0 ] = "json";
    
            // Install callback
            overwritten = window[ callbackName ];
            window[ callbackName ] = function() {
                responseContainer = arguments;
            };
    
            // Clean-up function (fires after converters)
            jqXHR.always(function() {
                // Restore preexisting value
                window[ callbackName ] = overwritten;
    
                // Save back as free
                if ( s[ callbackName ] ) {
                    // make sure that re-using the options doesn't screw things around
                    s.jsonpCallback = originalSettings.jsonpCallback;
    
                    // save the callback name for future use
                    oldCallbacks.push( callbackName );
                }
    
                // Call if it was a function and we have a response
                if ( responseContainer && jQuery.isFunction( overwritten ) ) {
                    overwritten( responseContainer[ 0 ] );
                }
    
                responseContainer = overwritten = undefined;
            });
    
            // Delegate to script
            return "script";
        }
    });
    
    
    
    
    // data: string of html
    // context (optional): If specified, the fragment will be created in this context, defaults to document
    // keepScripts (optional): If true, will include scripts passed in the html string
    jQuery.parseHTML = function( data, context, keepScripts ) {
        if ( !data || typeof data !== "string" ) {
            return null;
        }
        if ( typeof context === "boolean" ) {
            keepScripts = context;
            context = false;
        }
        context = context || document;
    
        var parsed = rsingleTag.exec( data ),
            scripts = !keepScripts && [];
    
        // Single tag
        if ( parsed ) {
            return [ context.createElement( parsed[1] ) ];
        }
    
        parsed = jQuery.buildFragment( [ data ], context, scripts );
    
        if ( scripts && scripts.length ) {
            jQuery( scripts ).remove();
        }
    
        return jQuery.merge( [], parsed.childNodes );
    };
    
    
    // Keep a copy of the old load method
    var _load = jQuery.fn.load;
    
    /**
     * Load a url into a page
     */
    jQuery.fn.load = function( url, params, callback ) {
        if ( typeof url !== "string" && _load ) {
            return _load.apply( this, arguments );
        }
    
        var selector, type, response,
            self = this,
            off = url.indexOf(" ");
    
        if ( off >= 0 ) {
            selector = jQuery.trim( url.slice( off ) );
            url = url.slice( 0, off );
        }
    
        // If it's a function
        if ( jQuery.isFunction( params ) ) {
    
            // We assume that it's the callback
            callback = params;
            params = undefined;
    
        // Otherwise, build a param string
        } else if ( params && typeof params === "object" ) {
            type = "POST";
        }
    
        // If we have elements to modify, make the request
        if ( self.length > 0 ) {
            jQuery.ajax({
                url: url,
    
                // if "type" variable is undefined, then "GET" method will be used
                type: type,
                dataType: "html",
                data: params
            }).done(function( responseText ) {
    
                // Save response for use in complete callback
                response = arguments;
    
                self.html( selector ?
    
                    // If a selector was specified, locate the right elements in a dummy div
                    // Exclude scripts to avoid IE 'Permission Denied' errors
                    jQuery("<div>").append( jQuery.parseHTML( responseText ) ).find( selector ) :
    
                    // Otherwise use the full result
                    responseText );
    
            }).complete( callback && function( jqXHR, status ) {
                self.each( callback, response || [ jqXHR.responseText, status, jqXHR ] );
            });
        }
    
        return this;
    };
    
    
    
    
    jQuery.expr.filters.animated = function( elem ) {
        return jQuery.grep(jQuery.timers, function( fn ) {
            return elem === fn.elem;
        }).length;
    };
    
    
    
    
    var docElem = window.document.documentElement;
    
    /**
     * Gets a window from an element
     */
    function getWindow( elem ) {
        return jQuery.isWindow( elem ) ? elem : elem.nodeType === 9 && elem.defaultView;
    }
    
    jQuery.offset = {
        setOffset: function( elem, options, i ) {
            var curPosition, curLeft, curCSSTop, curTop, curOffset, curCSSLeft, calculatePosition,
                position = jQuery.css( elem, "position" ),
                curElem = jQuery( elem ),
                props = {};
    
            // Set position first, in-case top/left are set even on static elem
            if ( position === "static" ) {
                elem.style.position = "relative";
            }
    
            curOffset = curElem.offset();
            curCSSTop = jQuery.css( elem, "top" );
            curCSSLeft = jQuery.css( elem, "left" );
            calculatePosition = ( position === "absolute" || position === "fixed" ) &&
                ( curCSSTop + curCSSLeft ).indexOf("auto") > -1;
    
            // Need to be able to calculate position if either top or left is auto and position is either absolute or fixed
            if ( calculatePosition ) {
                curPosition = curElem.position();
                curTop = curPosition.top;
                curLeft = curPosition.left;
    
            } else {
                curTop = parseFloat( curCSSTop ) || 0;
                curLeft = parseFloat( curCSSLeft ) || 0;
            }
    
            if ( jQuery.isFunction( options ) ) {
                options = options.call( elem, i, curOffset );
            }
    
            if ( options.top != null ) {
                props.top = ( options.top - curOffset.top ) + curTop;
            }
            if ( options.left != null ) {
                props.left = ( options.left - curOffset.left ) + curLeft;
            }
    
            if ( "using" in options ) {
                options.using.call( elem, props );
    
            } else {
                curElem.css( props );
            }
        }
    };
    
    jQuery.fn.extend({
        offset: function( options ) {
            if ( arguments.length ) {
                return options === undefined ?
                    this :
                    this.each(function( i ) {
                        jQuery.offset.setOffset( this, options, i );
                    });
            }
    
            var docElem, win,
                elem = this[ 0 ],
                box = { top: 0, left: 0 },
                doc = elem && elem.ownerDocument;
    
            if ( !doc ) {
                return;
            }
    
            docElem = doc.documentElement;
    
            // Make sure it's not a disconnected DOM node
            if ( !jQuery.contains( docElem, elem ) ) {
                return box;
            }
    
            // If we don't have gBCR, just use 0,0 rather than error
            // BlackBerry 5, iOS 3 (original iPhone)
            if ( typeof elem.getBoundingClientRect !== strundefined ) {
                box = elem.getBoundingClientRect();
            }
            win = getWindow( doc );
            return {
                top: box.top + win.pageYOffset - docElem.clientTop,
                left: box.left + win.pageXOffset - docElem.clientLeft
            };
        },
    
        position: function() {
            if ( !this[ 0 ] ) {
                return;
            }
    
            var offsetParent, offset,
                elem = this[ 0 ],
                parentOffset = { top: 0, left: 0 };
    
            // Fixed elements are offset from window (parentOffset = {top:0, left: 0}, because it is its only offset parent
            if ( jQuery.css( elem, "position" ) === "fixed" ) {
                // We assume that getBoundingClientRect is available when computed position is fixed
                offset = elem.getBoundingClientRect();
    
            } else {
                // Get *real* offsetParent
                offsetParent = this.offsetParent();
    
                // Get correct offsets
                offset = this.offset();
                if ( !jQuery.nodeName( offsetParent[ 0 ], "html" ) ) {
                    parentOffset = offsetParent.offset();
                }
    
                // Add offsetParent borders
                parentOffset.top += jQuery.css( offsetParent[ 0 ], "borderTopWidth", true );
                parentOffset.left += jQuery.css( offsetParent[ 0 ], "borderLeftWidth", true );
            }
    
            // Subtract parent offsets and element margins
            return {
                top: offset.top - parentOffset.top - jQuery.css( elem, "marginTop", true ),
                left: offset.left - parentOffset.left - jQuery.css( elem, "marginLeft", true )
            };
        },
    
        offsetParent: function() {
            return this.map(function() {
                var offsetParent = this.offsetParent || docElem;
    
                while ( offsetParent && ( !jQuery.nodeName( offsetParent, "html" ) && jQuery.css( offsetParent, "position" ) === "static" ) ) {
                    offsetParent = offsetParent.offsetParent;
                }
    
                return offsetParent || docElem;
            });
        }
    });
    
    // Create scrollLeft and scrollTop methods
    jQuery.each( { scrollLeft: "pageXOffset", scrollTop: "pageYOffset" }, function( method, prop ) {
        var top = "pageYOffset" === prop;
    
        jQuery.fn[ method ] = function( val ) {
            return access( this, function( elem, method, val ) {
                var win = getWindow( elem );
    
                if ( val === undefined ) {
                    return win ? win[ prop ] : elem[ method ];
                }
    
                if ( win ) {
                    win.scrollTo(
                        !top ? val : window.pageXOffset,
                        top ? val : window.pageYOffset
                    );
    
                } else {
                    elem[ method ] = val;
                }
            }, method, val, arguments.length, null );
        };
    });
    
    // Add the top/left cssHooks using jQuery.fn.position
    // Webkit bug: https://bugs.webkit.org/show_bug.cgi?id=29084
    // getComputedStyle returns percent when specified for top/left/bottom/right
    // rather than make the css module depend on the offset module, we just check for it here
    jQuery.each( [ "top", "left" ], function( i, prop ) {
        jQuery.cssHooks[ prop ] = addGetHookIf( support.pixelPosition,
            function( elem, computed ) {
                if ( computed ) {
                    computed = curCSS( elem, prop );
                    // if curCSS returns percentage, fallback to offset
                    return rnumnonpx.test( computed ) ?
                        jQuery( elem ).position()[ prop ] + "px" :
                        computed;
                }
            }
        );
    });
    
    
    // Create innerHeight, innerWidth, height, width, outerHeight and outerWidth methods
    jQuery.each( { Height: "height", Width: "width" }, function( name, type ) {
        jQuery.each( { padding: "inner" + name, content: type, "": "outer" + name }, function( defaultExtra, funcName ) {
            // margin is only for outerHeight, outerWidth
            jQuery.fn[ funcName ] = function( margin, value ) {
                var chainable = arguments.length && ( defaultExtra || typeof margin !== "boolean" ),
                    extra = defaultExtra || ( margin === true || value === true ? "margin" : "border" );
    
                return access( this, function( elem, type, value ) {
                    var doc;
    
                    if ( jQuery.isWindow( elem ) ) {
                        // As of 5/8/2012 this will yield incorrect results for Mobile Safari, but there
                        // isn't a whole lot we can do. See pull request at this URL for discussion:
                        // https://github.com/jquery/jquery/pull/764
                        return elem.document.documentElement[ "client" + name ];
                    }
    
                    // Get document width or height
                    if ( elem.nodeType === 9 ) {
                        doc = elem.documentElement;
    
                        // Either scroll[Width/Height] or offset[Width/Height] or client[Width/Height],
                        // whichever is greatest
                        return Math.max(
                            elem.body[ "scroll" + name ], doc[ "scroll" + name ],
                            elem.body[ "offset" + name ], doc[ "offset" + name ],
                            doc[ "client" + name ]
                        );
                    }
    
                    return value === undefined ?
                        // Get width or height on the element, requesting but not forcing parseFloat
                        jQuery.css( elem, type, extra ) :
    
                        // Set width or height on the element
                        jQuery.style( elem, type, value, extra );
                }, type, chainable ? margin : undefined, chainable, null );
            };
        });
    });
    
    
    // The number of elements contained in the matched element set
    jQuery.fn.size = function() {
        return this.length;
    };
    
    jQuery.fn.andSelf = jQuery.fn.addBack;
    
    
    
    
    // Register as a named AMD module, since jQuery can be concatenated with other
    // files that may use define, but not via a proper concatenation script that
    // understands anonymous AMD modules. A named AMD is safest and most robust
    // way to register. Lowercase jquery is used because AMD module names are
    // derived from file names, and jQuery is normally delivered in a lowercase
    // file name. Do this after creating the global so that if an AMD module wants
    // to call noConflict to hide this version of jQuery, it will work.
    
    // Note that for maximum portability, libraries that are not jQuery should
    // declare themselves as anonymous modules, and avoid setting a global if an
    // AMD loader is present. jQuery is a special case. For more information, see
    // https://github.com/jrburke/requirejs/wiki/Updating-existing-libraries#wiki-anon
    
    if ( typeof define === "function" && define.amd ) {
        define( "jquery", [], function() {
            return jQuery;
        });
    }
    
    
    
    
    var
        // Map over jQuery in case of overwrite
        _jQuery = window.jQuery,
    
        // Map over the $ in case of overwrite
        _$ = window.$;
    
    jQuery.noConflict = function( deep ) {
        if ( window.$ === jQuery ) {
            window.$ = _$;
        }
    
        if ( deep && window.jQuery === jQuery ) {
            window.jQuery = _jQuery;
        }
    
        return jQuery;
    };
    
    // Expose jQuery and $ identifiers, even in
    // AMD (#7102#comment:10, https://github.com/jquery/jquery/pull/557)
    // and CommonJS for browser emulators (#13566)
    if ( typeof noGlobal === strundefined ) {
        window.jQuery = window.$ = jQuery;
    }
    
    
    
    
    return jQuery;
    
    }));
    
    },{}],40:[function(require,module,exports){
    (function (process){
    var jws = require('jws');
    
    module.exports.decode = function (jwt) {
      var decoded = jws.decode(jwt, {json: true});
      return decoded && decoded.payload;
    };
    
    module.exports.sign = function(payload, secretOrPrivateKey, options) {
      options = options || {};
    
      var header = ((typeof options.headers === 'object') && options.headers) || {};
      header.typ = 'JWT';
      header.alg = options.algorithm || 'HS256';
    
      if (options.header) {
        Object.keys(options.header).forEach(function (k) {
          header[k] = options.header[k];
        });
      }
    
      if (!options.noTimestamp) {
        payload.iat = Math.floor(Date.now() / 1000);
      }
    
      if (options.expiresInMinutes) {
        var ms = options.expiresInMinutes * 60;
        payload.exp = payload.iat + ms;
      }
    
      if (options.audience)
        payload.aud = options.audience;
    
      if (options.issuer)
        payload.iss = options.issuer;
    
      if (options.subject)
        payload.sub = options.subject;
    
      var signed = jws.sign({header: header, payload: payload, secret: secretOrPrivateKey});
    
      return signed;
    };
    
    module.exports.verify = function(jwtString, secretOrPublicKey, options, callback) {
      if ((typeof options === 'function') && !callback) {
        callback = options;
        options = {};
      }
    
      if (!options) options = {};
    
      if (callback) {
        var done = function() {
          var args = Array.prototype.slice.call(arguments, 0)
          return process.nextTick(function() {
              callback.apply(null, args)
          });
        };
      } else {
        var done = function(err, data) {
          if (err) throw err;
          return data;
        };
      }
    
      if (!jwtString)
        return done(new JsonWebTokenError('jwt must be provided'));
    
      var parts = jwtString.split('.');
      if (parts.length !== 3)
        return done(new JsonWebTokenError('jwt malformed'));
    
      if (parts[2].trim() === '' && secretOrPublicKey)
        return done(new JsonWebTokenError('jwt signature is required'));
    
      var valid;
      try {
        valid = jws.verify(jwtString, secretOrPublicKey);
      }
      catch (e) {
        return done(e);
      }
    
      if (!valid)
        return done(new JsonWebTokenError('invalid signature'));
    
      var payload;
    
      try {
       payload = this.decode(jwtString);
      } catch(err) {
        return done(err);
      }
    
      if (payload.exp) {
        if (Math.floor(Date.now() / 1000) >= payload.exp)
          return done(new TokenExpiredError('jwt expired', new Date(payload.exp * 1000)));
      }
    
      if (options.audience) {
        var audiences = Array.isArray(options.audience)? options.audience : [options.audience];
        var target = Array.isArray(payload.aud) ? payload.aud : [payload.aud];
        
        var match = target.some(function(aud) { return audiences.indexOf(aud) != -1; });
    
        if (!match)
          return done(new JsonWebTokenError('jwt audience invalid. expected: ' + payload.aud));
      }
    
      if (options.issuer) {
        if (payload.iss !== options.issuer)
          return done(new JsonWebTokenError('jwt issuer invalid. expected: ' + payload.iss));
      }
    
      return done(null, payload);
    };
    
    var JsonWebTokenError = module.exports.JsonWebTokenError = function (message, error) {
      Error.call(this, message);
      this.name = 'JsonWebTokenError';
      this.message = message;
      if (error) this.inner = error;
    };
    
    JsonWebTokenError.prototype = Object.create(Error.prototype);
    JsonWebTokenError.prototype.constructor = JsonWebTokenError;
    
    var TokenExpiredError = module.exports.TokenExpiredError = function (message, expiredAt) {
      JsonWebTokenError.call(this, message);
      this.name = 'TokenExpiredError';
      this.expiredAt = expiredAt;
    };
    TokenExpiredError.prototype = Object.create(JsonWebTokenError.prototype);
    TokenExpiredError.prototype.constructor = TokenExpiredError;
    
    }).call(this,require('_process'))
    },{"_process":22,"jws":41}],41:[function(require,module,exports){
    (function (process){
    /*global process, exports*/
    var Buffer = require('buffer').Buffer;
    var Stream = require('stream');
    var util = require('util');
    var base64url = require('base64url');
    var jwa = require('jwa');
    
    var ALGORITHMS = [
      'HS256', 'HS384', 'HS512',
      'RS256', 'RS384', 'RS512',
      'ES256', 'ES384', 'ES512',
    ];
    
    function toString(obj) {
      if (typeof obj === 'string')
        return obj;
      if (typeof obj === 'number' || Buffer.isBuffer(obj))
        return obj.toString();
      return JSON.stringify(obj);
    }
    
    function jwsSecuredInput(header, payload) {
      var encodedHeader = base64url(toString(header));
      var encodedPayload = base64url(toString(payload));
      return util.format('%s.%s', encodedHeader, encodedPayload);
    }
    
    function jwsSign(opts) {
      var header = opts.header;
      var payload = opts.payload;
      var secretOrKey = opts.secret || opts.privateKey;
      var algo = jwa(header.alg);
      var securedInput = jwsSecuredInput(header, payload);
      var signature = algo.sign(securedInput, secretOrKey);
      return util.format('%s.%s', securedInput, signature);
    }
    
    function isObject(thing) {
      return Object.prototype.toString.call(thing) === '[object Object]';
    }
    
    function safeJsonParse(thing) {
      if (isObject(thing))
        return thing;
      try { return JSON.parse(thing) }
      catch (e) { return undefined }
    }
    
    function headerFromJWS(jwsSig) {
      var encodedHeader = jwsSig.split('.', 1)[0];
      return safeJsonParse(base64url.decode(encodedHeader));
    }
    
    function securedInputFromJWS(jwsSig) {
      return jwsSig.split('.', 2).join('.');
    }
    
    function algoFromJWS(jwsSig) {
      var err;
      var header = headerFromJWS(jwsSig);
      if (typeof header != 'object') {
        err = new Error("Invalid token: no header in signature '" + jwsSig + "'");
        err.code = "MISSING_HEADER";
        err.signature = jwsSig;
        throw err;
      }
      if (!header.alg) {
        err = new Error("Missing `alg` field in header for signature '"+ jwsSig +"'");
        err.code = "MISSING_ALGORITHM";
        err.header = header;
        err.signature = jwsSig;
        throw err;
      }
      return header.alg;
    }
    
    function signatureFromJWS(jwsSig) {
      return jwsSig.split('.')[2];
    }
    
    function payloadFromJWS(jwsSig) {
      var payload = jwsSig.split('.')[1];
      return base64url.decode(payload);
    }
    
    var JWS_REGEX = /^[a-zA-Z0-9\-_]+?\.[a-zA-Z0-9\-_]+?\.([a-zA-Z0-9\-_]+)?$/;
    function isValidJws(string) {
      if (!JWS_REGEX.test(string))
        return false;
      if (!headerFromJWS(string))
        return false;
      return true;
    }
    
    function jwsVerify(jwsSig, secretOrKey) {
      jwsSig = toString(jwsSig);
      var signature = signatureFromJWS(jwsSig);
      var securedInput = securedInputFromJWS(jwsSig);
      var algo = jwa(algoFromJWS(jwsSig));
      return algo.verify(securedInput, signature, secretOrKey);
    }
    
    function jwsDecode(jwsSig, opts) {
      opts = opts || {};
      jwsSig = toString(jwsSig);
      if (!isValidJws(jwsSig))
        return null;
      var header = headerFromJWS(jwsSig);
      if (!header)
        return null;
      var payload = payloadFromJWS(jwsSig);
      if (header.typ === 'JWT' || opts.json)
        payload = JSON.parse(payload);
      return {
        header: header,
        payload: payload,
        signature: signatureFromJWS(jwsSig),
      };
    }
    
    function SignStream(opts) {
      var secret = opts.secret||opts.privateKey||opts.key;
      var secretStream = new DataStream(secret);
      this.readable = true;
      this.header = opts.header;
      this.secret = this.privateKey = this.key = secretStream;
      this.payload = new DataStream(opts.payload);
      this.secret.once('close', function () {
        if (!this.payload.writable && this.readable)
          this.sign();
      }.bind(this));
    
      this.payload.once('close', function () {
        if (!this.secret.writable && this.readable)
          this.sign();
      }.bind(this));
    }
    util.inherits(SignStream, Stream);
    SignStream.prototype.sign = function sign() {
      var signature = jwsSign({
        header: this.header,
        payload: this.payload.buffer,
        secret: this.secret.buffer,
      });
      this.emit('done', signature);
      this.emit('data', signature);
      this.emit('end');
      this.readable = false;
      return signature;
    };
    
    function VerifyStream(opts) {
      opts = opts || {};
      var secretOrKey = opts.secret||opts.publicKey||opts.key;
      var secretStream = new DataStream(secretOrKey);
      this.readable = true;
      this.secret = this.publicKey = this.key = secretStream;
      this.signature = new DataStream(opts.signature);
      this.secret.once('close', function () {
        if (!this.signature.writable && this.readable)
          this.verify();
      }.bind(this));
    
      this.signature.once('close', function () {
        if (!this.secret.writable && this.readable)
          this.verify();
      }.bind(this));
    }
    util.inherits(VerifyStream, Stream);
    VerifyStream.prototype.verify = function verify() {
      var valid = jwsVerify(this.signature.buffer, this.key.buffer);
      var obj = jwsDecode(this.signature.buffer);
      this.emit('done', valid, obj);
      this.emit('data', valid);
      this.emit('end');
      this.readable = false;
      return valid;
    };
    
    function DataStream(data) {
      this.buffer = Buffer(data||0);
      this.writable = true;
      this.readable = true;
      if (!data)
        return this;
      if (typeof data.pipe === 'function')
        data.pipe(this);
      else if (data.length) {
        this.writable = false;
        process.nextTick(function () {
          this.buffer = data;
          this.emit('end', data);
          this.readable = false;
          this.emit('close');
        }.bind(this));
      }
    }
    util.inherits(DataStream, Stream);
    
    DataStream.prototype.write = function write(data) {
      this.buffer = Buffer.concat([this.buffer, Buffer(data)]);
      this.emit('data', data);
    };
    
    DataStream.prototype.end = function end(data) {
      if (data)
        this.write(data);
      this.emit('end', data);
      this.emit('close');
      this.writable = false;
      this.readable = false;
    };
    
    exports.ALGORITHMS = ALGORITHMS;
    exports.sign = jwsSign;
    exports.verify = jwsVerify;
    exports.decode = jwsDecode;
    exports.isValid = isValidJws;
    exports.createSign = function createSign(opts) {
      return new SignStream(opts);
    };
    exports.createVerify = function createVerify(opts) {
      return new VerifyStream(opts);
    };
    
    }).call(this,require('_process'))
    },{"_process":22,"base64url":42,"buffer":3,"jwa":43,"stream":35,"util":37}],42:[function(require,module,exports){
    (function (Buffer){
    function fromBase64(base64string) {
      return (
        base64string
          .replace(/=/g, '')
          .replace(/\+/g, '-')
          .replace(/\//g, '_')
      );
    }
    
    function toBase64(base64UrlString) {
      if (Buffer.isBuffer(base64UrlString))
        base64UrlString = base64UrlString.toString()
    
      var b64str = padString(base64UrlString)
        .replace(/\-/g, '+')
        .replace(/_/g, '/');
      return b64str;
    }
    
    function padString(string) {
      var segmentLength = 4;
      var stringLength = string.length;
      var diff = string.length % segmentLength;
      if (!diff)
        return string;
      var position = stringLength;
      var padLength = segmentLength - diff;
      var paddedStringLength = stringLength + padLength;
      var buffer = Buffer(paddedStringLength);
      buffer.write(string);
      while (padLength--)
        buffer.write('=', position++);
      return buffer.toString();
    }
    
    function decodeBase64Url(base64UrlString, encoding) {
      return Buffer(toBase64(base64UrlString), 'base64').toString(encoding);
    }
    
    function base64url(stringOrBuffer) {
      return fromBase64(Buffer(stringOrBuffer).toString('base64'));
    }
    
    function toBuffer(base64string) {
      return Buffer(toBase64(base64string), 'base64');
    }
    
    base64url.toBase64 = toBase64;
    base64url.fromBase64 = fromBase64;
    base64url.decode = decodeBase64Url;
    base64url.toBuffer = toBuffer;
    
    module.exports = base64url;
    
    }).call(this,require("buffer").Buffer)
    },{"buffer":3}],43:[function(require,module,exports){
    (function (Buffer){
    var base64url = require('base64url');
    var crypto = require('crypto');
    var util = require('util');
    
    var MSG_INVALID_ALGORITHM = '"%s" is not a valid algorithm.\n  Supported algorithms are:\n  "HS256", "HS384", "HS512", "RS256", "RS384", "RS512" and "none".'
    var MSG_INVALID_SECRET = 'secret must be a string or buffer';
    var MSG_INVALID_KEY = 'key must be a string or buffer';
    
    function typeError(template) {
      var args = [].slice.call(arguments, 1);
      var errMsg = util.format.bind(util, template).apply(null, args);
      return new TypeError(errMsg);
    }
    
    function bufferOrString(obj) {
      return Buffer.isBuffer(obj) || typeof obj === 'string';
    }
    
    function normalizeInput(thing) {
      if (!bufferOrString(thing))
        thing = JSON.stringify(thing);
      return thing;
    }
    
    function createHmacSigner(bits) {
      return function sign(thing, secret) {
        if (!bufferOrString(secret))
          throw typeError(MSG_INVALID_SECRET);
        thing = normalizeInput(thing);
        var hmac = crypto.createHmac('SHA' + bits, secret);
        var sig = (hmac.update(thing), hmac.digest('base64'))
        return base64url.fromBase64(sig);
      }
    }
    
    function createHmacVerifier(bits) {
      return function verify(thing, signature, secret) {
        var computedSig = createHmacSigner(bits)(thing, secret);
        return signature === computedSig;
      }
    }
    
    function createKeySigner(bits) {
      return function sign(thing, privateKey) {
        if (!bufferOrString(privateKey))
          throw typeError(MSG_INVALID_KEY);
        thing = normalizeInput(thing);
        var signer = crypto.createSign('RSA-SHA' + bits);
        var sig = (signer.update(thing), signer.sign(privateKey, 'base64'));
        return base64url.fromBase64(sig);
      }
    }
    
    function createKeyVerifier(bits) {
      return function verify(thing, signature, publicKey) {
        if (!bufferOrString(publicKey))
          throw typeError(MSG_INVALID_KEY);
        thing = normalizeInput(thing);
        signature = base64url.toBase64(signature);
        var verifier = crypto.createVerify('RSA-SHA' + bits);
        verifier.update(thing);
        return verifier.verify(publicKey, signature, 'base64');
      }
    }
    
    function createNoneSigner() {
      return function sign() {
        return '';
      }
    }
    
    function createNoneVerifier() {
      return function verify(thing, signature) {
        return signature === '';
      }
    }
    
    module.exports = function jwa(algorithm) {
      var signerFactories = {
        hs: createHmacSigner,
        rs: createKeySigner,
        es: createKeySigner,
        none: createNoneSigner,
      }
      var verifierFactories = {
        hs: createHmacVerifier,
        rs: createKeyVerifier,
        es: createKeyVerifier,
        none: createNoneVerifier,
      }
      var match = algorithm.match(/(RS|ES|HS|none)(256|384|512)?/i);
      if (!match)
        throw typeError(MSG_INVALID_ALGORITHM, algorithm);
      var algo = match[1].toLowerCase();
      var bits = match[2];
    
      return {
        sign: signerFactories[algo](bits),
        verify: verifierFactories[algo](bits),
      }
    };
    }).call(this,require("buffer").Buffer)
    },{"base64url":42,"buffer":3,"crypto":9,"util":37}],44:[function(require,module,exports){
    (function (process){
    (function() {
        var smart = require('../client/entry');
        var jquery = _jQuery = require('jquery');
    
        // Patch jQuery AJAX mechanism to receive blob objects via XMLHttpRequest 2. Based on:
        //    https://gist.github.com/aaronk6/bff7cc600d863d31a7bf
        //    http://www.artandlogic.com/blog/2013/11/jquery-ajax-blobs-and-array-buffers/
    
        /**
         * Register ajax transports for blob send/recieve and array buffer send/receive via XMLHttpRequest Level 2
         * within the comfortable framework of the jquery ajax request, with full support for promises.
         *
         * Notice the +* in the dataType string? The + indicates we want this transport to be prepended to the list
         * of potential transports (so it gets first dibs if the request passes the conditions within to provide the
         * ajax transport, preventing the standard transport from hogging the request), and the * indicates that
         * potentially any request with any dataType might want to use the transports provided herein.
         *
         * Remember to specify 'processData:false' in the ajax options when attempting to send a blob or arraybuffer -
         * otherwise jquery will try (and fail) to convert the blob or buffer into a query string.
         */
        jquery.ajaxTransport("+*", function(options, originalOptions, jqXHR){
            // Test for the conditions that mean we can/want to send/receive blobs or arraybuffers - we need XMLHttpRequest
            // level 2 (so feature-detect against window.FormData), feature detect against window.Blob or window.ArrayBuffer,
            // and then check to see if the dataType is blob/arraybuffer or the data itself is a Blob/ArrayBuffer
            if (window.FormData && ((options.dataType && (options.dataType === 'blob' || options.dataType === 'arraybuffer')) ||
                (options.data && ((window.Blob && options.data instanceof Blob) ||
                    (window.ArrayBuffer && options.data instanceof ArrayBuffer)))
                ))
            {
                return {
                    /**
                     * Return a transport capable of sending and/or receiving blobs - in this case, we instantiate
                     * a new XMLHttpRequest and use it to actually perform the request, and funnel the result back
                     * into the jquery complete callback (such as the success function, done blocks, etc.)
                     *
                     * @param headers
                     * @param completeCallback
                     */
                    send: function(headers, completeCallback){
                        var xhr = new XMLHttpRequest(),
                            url = options.url || window.location.href,
                            type = options.type || 'GET',
                            dataType = options.dataType || 'text',
                            data = options.data || null,
                            async = options.async || true,
                            key;
    
                        xhr.addEventListener('load', function(){
                            var response = {}, status, isSuccess;
    
                            isSuccess = xhr.status >= 200 && xhr.status < 300 || xhr.status === 304;
    
                            if (isSuccess) {
                                response[dataType] = xhr.response;
                            } else {
                                // In case an error occured we assume that the response body contains
                                // text data - so let's convert the binary data to a string which we can
                                // pass to the complete callback.
                                response.text = String.fromCharCode.apply(null, new Uint8Array(xhr.response));
                            }
    
                            completeCallback(xhr.status, xhr.statusText, response, xhr.getAllResponseHeaders());
                        });
    
                        xhr.open(type, url, async);
                        xhr.responseType = dataType;
    
                        for (key in headers) {
                            if (headers.hasOwnProperty(key)) xhr.setRequestHeader(key, headers[key]);
                        }
                        xhr.send(data);
                    },
                    abort: function(){
                        jqXHR.abort();
                    }
                };
            }
        });
        
        if (!process.browser) {
          var windowObj = require('jsdom').jsdom().createWindow();
          jquery = jquery(windowObj);
        }
        
        var defer = function(){
            pr = jquery.Deferred();
            pr.promise = pr.promise();
            return pr;
        };
        var adapter = {
            defer: defer,
            http: function(args) {
                var ret = jquery.Deferred();
                var opts = {
                    type: args.method,
                    url: args.url,
                    dataType: args.dataType || "json",
                    headers: args.headers || {},
                    data: args.data
                };
                jquery.ajax(opts)
                    .done(ret.resolve)
                    .fail(ret.reject);
                return ret.promise();
            },
            fhirjs: require('../../lib/jqFhir.js')
        };
    
        smart(adapter);
    
    }).call(this);
    
    }).call(this,require('_process'))
    },{"../../lib/jqFhir.js":1,"../client/entry":48,"_process":22,"jquery":39,"jsdom":2}],45:[function(require,module,exports){
    var adapter;
    
    var Adapter = module.exports =  {debug: true}
    
    Adapter.set = function (newAdapter) {
        adapter = newAdapter;
    };
    
    Adapter.get = function () {
        return adapter;
    };
    
    },{}],46:[function(require,module,exports){
    (function (process){
    var Adapter = require('./adapter');
    var FhirClient = require('./client');
    var Guid = require('./guid');
    var jwt = require('jsonwebtoken');
    
    var BBClient = module.exports =  {debug: true}
    
    function urlParam(p, forceArray) {
      if (forceArray === undefined) {
        forceArray = false;
      }
    
      var query = location.search.substr(1);
      var data = query.split("&");
      var result = [];
    
      for(var i=0; i<data.length; i++) {
        var item = data[i].split("=");
        if (item[0] === p) {
          var res = item[1].replace(/\+/g, '%20');
          result.push(decodeURIComponent(res));
        }
      }
    
      if (forceArray) {
        return result;
      }
      if (result.length === 0){
        return null;
      }
      return result[0];
    }
    
    function stripTrailingSlash(str) {
        if(str.substr(-1) === '/') {
            return str.substr(0, str.length - 1);
        }
        return str;
    }
    
    /**
    * Get the previous token stored in sessionStorage
    * based on fullSessionStorageSupport flag.
    * @return object JSON tokenResponse
    */
    function getPreviousToken(){
      var token;
      
      if (BBClient.settings.fullSessionStorageSupport) {
        token = sessionStorage.tokenResponse;
        return JSON.parse(token);
      } else {
        var state = urlParam('state');
        return JSON.parse(sessionStorage[state]).tokenResponse;
      }
    }
    
    function completeTokenFlow(hash){
      if (!hash){
        hash = window.location.hash;
      }
      var ret = Adapter.get().defer();
    
      process.nextTick(function(){
        var oauthResult = hash.match(/#(.*)/);
        oauthResult = oauthResult ? oauthResult[1] : "";
        oauthResult = oauthResult.split(/&/);
        var authorization = {};
        for (var i = 0; i < oauthResult.length; i++){
          var kv = oauthResult[i].split(/=/);
          if (kv[0].length > 0 && kv[1]) {
            authorization[decodeURIComponent(kv[0])] = decodeURIComponent(kv[1]);
          }
        }
        ret.resolve(authorization);
      });
    
      return ret.promise;
    }
    
    function completeCodeFlow(params){
      if (!params){
        params = {
          code: urlParam('code'),
          state: urlParam('state')
        };
      }
      
      var ret = Adapter.get().defer();
      var state = JSON.parse(sessionStorage[params.state]);
    
      if (window.history.replaceState && BBClient.settings.replaceBrowserHistory){
        window.history.replaceState({}, "", window.location.toString().replace(window.location.search, ""));
      } 
    
      // Using window.history.pushState to append state to the query param.
      // This will allow session data to be retrieved via the state param.
      if (window.history.pushState && !BBClient.settings.fullSessionStorageSupport) {
        
        var queryParam = window.location.search;
        if (window.location.search.indexOf('state') == -1) {
          // Append state query param to URI for later.
          // state query param will be used to look up
          // token response upon page reload.
    
          queryParam += (window.location.search ? '&' : '?');
          queryParam += 'state=' + params.state;
          
          var url = window.location.protocol + '//' + 
                                 window.location.host + 
                                 window.location.pathname + 
                                 queryParam;
    
          window.history.pushState({}, "", url);
        }
      }
    
      var data = {
          code: params.code,
          grant_type: 'authorization_code',
          redirect_uri: state.client.redirect_uri
      };
    
      var headers = {};
    
      if (state.client.secret) {
        headers['Authorization'] = 'Basic ' + btoa(state.client.client_id + ':' + state.client.secret);
      } else {
        data['client_id'] = state.client.client_id;
      }
    
      Adapter.get().http({
        method: 'POST',
        url: state.provider.oauth2.token_uri,
        data: data,
        headers: headers
      }).then(function(authz){
           for (var i in params) {
              if (params.hasOwnProperty(i)) {
                 authz[i] = params[i];
              }
           }
           ret.resolve(authz);
      }, function(){
        console.log("failed to exchange code for access_token", arguments);
        ret.reject();
      });
    
      return ret.promise;
    }
    
    /**
     * This code is needed for the page refresh/reload workflow.
     * When the access token is nearing expriration or is expired,
     * this function will make an ajax POST call to obtain a new
     * access token using the current refresh token.
     * @return promise object
     */
    function completeTokenRefreshFlow() {
      var ret = Adapter.get().defer();
      var tokenResponse = getPreviousToken();
      var state = JSON.parse(sessionStorage[tokenResponse.state]);
      var refresh_token = tokenResponse.refresh_token;
    
      Adapter.get().http({
        method: 'POST',
        url: state.provider.oauth2.token_uri,
        data: {
          grant_type: 'refresh_token',
          refresh_token: refresh_token
        },
      }).then(function(authz) {
        authz = $.extend(tokenResponse, authz);
        ret.resolve(authz);
      }, function() {
        console.warn('Failed to exchange refresh_token for access_token', arguments);
        ret.reject('Failed to exchange refresh token for access token. ' +
          'Please close and re-launch the application again.');
      });
    
      return ret.promise;
    }
    
    function completePageReload(){
      var d = Adapter.get().defer();
      process.nextTick(function(){
        d.resolve(getPreviousToken());
      });
      return d;
    }
    
    function readyArgs(){
    
      var input = null;
      var callback = function(){};
      var errback = function(){};
    
      if (arguments.length === 0){
        throw "Can't call 'ready' without arguments";
      } else if (arguments.length === 1){
        callback = arguments[0];
      } else if (arguments.length === 2){
        if (typeof arguments[0] === 'function'){
          callback = arguments[0];
          errback = arguments[1];
        } else if (typeof arguments[0] === 'object'){
          input = arguments[0];
          callback = arguments[1];
        } else {
          throw "ready called with invalid arguments";
        }
      } else if (arguments.length === 3){
        input = arguments[0];
        callback = arguments[1];
        errback = arguments[2];
      } else {
        throw "ready called with invalid arguments";
      }
    
      return {
        input: input,
        callback: callback,
        errback: errback
      };
    }
    
    // Client settings
    BBClient.settings = {
      // Replaces the browser's current URL
      // using window.history.replaceState API.
      // Default to true
      replaceBrowserHistory: true,
      
      // When set to true, this variable will fully utilize
      // HTML5 sessionStorage API.
      // Default to true
      // This variable can be overriden to false by setting
      // FHIR.oauth2.settings.fullSessionStorageSupport = false.
      // When set to false, the sessionStorage will be keyed 
      // by a state variable. This is to allow the embedded IE browser
      // instances instantiated on a single thread to continue to
      // function without having sessionStorage data shared 
      // across the embedded IE instances.
      fullSessionStorageSupport: true
    };
    
    /**
    * Check the tokenResponse object to see if it is valid or not.
    * This is to handle the case of a refresh/reload of the page
    * after the token was already obtain.
    * @return boolean
    */
    function validTokenResponse() {
      if (BBClient.settings.fullSessionStorageSupport && sessionStorage.tokenResponse) {
        return true;
      } else {
        if (!BBClient.settings.fullSessionStorageSupport) {
          var state = urlParam('state') || (args.input && args.input.state);
          return (state && sessionStorage[state] && JSON.parse(sessionStorage[state]).tokenResponse);
        }
      }
      return false;
    }
    
    function isFakeOAuthToken(){
      if (validTokenResponse()) {
        var token = getPreviousToken();
        if (token && token.state) {
          var state = JSON.parse(sessionStorage[token.state]);
          return state.fake_token_response;
        }
      }
      return false;
    }
    
    BBClient.ready = function(input, callback, errback){
    
      var args = readyArgs.apply(this, arguments);
    
      // decide between token flow (implicit grant) and code flow (authorization code grant)
      var isCode = urlParam('code') || (args.input && args.input.code);
    
      var accessTokenResolver = null;
    
      if (isFakeOAuthToken()) {
        accessTokenResolver = completePageReload();
        // In order to remove the state query parameter in the URL, both replaceBrowserHistory
        // and fullSessionStorageSupport setting flags must be set to true. This allows querying the state
        // through sessionStorage. If the browser does not support the replaceState method for the History Web API,
        // or if either of the setting flags are false, the state property will be retrieved
        // from the state query parameter in the URL.
        if (window.history.replaceState
          && BBClient.settings.replaceBrowserHistory
          && BBClient.settings.fullSessionStorageSupport){
          window.history.replaceState({}, "", window.location.toString().replace(window.location.search, ""));
        }
      } else {
        if (validTokenResponse()) { // we're reloading after successful completion
          // Check if 2 minutes from access token expiration timestamp
          var tokenResponse = getPreviousToken();
          var payloadCheck = jwt.decode(tokenResponse.access_token);
          var nearExpTime = Math.floor(Date.now() / 1000) >= (payloadCheck['exp'] - 120);
    
          if (tokenResponse.refresh_token
            && tokenResponse.scope.indexOf('online_access') > -1
            && nearExpTime) { // refresh token flow
            accessTokenResolver = completeTokenRefreshFlow();
          } else { // existing access token flow
            accessTokenResolver = completePageReload();
          }
        } else if (isCode) { // code flow
          accessTokenResolver = completeCodeFlow(args.input);
        } else { // token flow
          accessTokenResolver = completeTokenFlow(args.input);
        }
      }
      accessTokenResolver.done(function(tokenResponse){
    
        if (!tokenResponse || !tokenResponse.state) {
          return args.errback("No 'state' parameter found in authorization response.");
        }
    
        // Save the tokenReponse object into sessionStorage
        if (BBClient.settings.fullSessionStorageSupport) {
          sessionStorage.tokenResponse = JSON.stringify(tokenResponse);
        } else {
          //Save the tokenResponse object and the state into sessionStorage keyed by state
          var combinedObject = $.extend(true, JSON.parse(sessionStorage[tokenResponse.state]), { 'tokenResponse' : tokenResponse });
          sessionStorage[tokenResponse.state] = JSON.stringify(combinedObject);
        }
    
        var state = JSON.parse(sessionStorage[tokenResponse.state]);
        if (state.fake_token_response) {
          tokenResponse = state.fake_token_response;
        }
    
        var fhirClientParams = {
          serviceUrl: state.provider.url,
          patientId: tokenResponse.patient
        };
        
        if (tokenResponse.id_token) {
            var id_token = tokenResponse.id_token;
            var payload = jwt.decode(id_token);
            fhirClientParams["userId"] = payload["profile"]; 
        }
    
        if (tokenResponse.access_token !== undefined) {
          fhirClientParams.auth = {
            type: 'bearer',
            token: tokenResponse.access_token
          };
        } else if (!state.fake_token_response){
          return args.errback("Failed to obtain access token.");
        }
    
        var ret = FhirClient(fhirClientParams);
        ret.state = JSON.parse(JSON.stringify(state));
        ret.tokenResponse = JSON.parse(JSON.stringify(tokenResponse));
        args.callback(ret);
    
      }).fail(function(ret){
        ret ? args.errback(ret) : args.errback("Failed to obtain access token.");
      });
    
    };
    
    function providers(fhirServiceUrl, provider, callback, errback){
    
      // Shim for pre-OAuth2 launch parameters
      if (isBypassOAuth()){
        process.nextTick(function(){
          bypassOAuth(fhirServiceUrl, callback);
        });
        return;
      }
    
      // Skip conformance statement introspection when overriding provider setting are available
      if (provider) {
        provider['url'] = fhirServiceUrl;
        process.nextTick(function(){
          callback && callback(provider);
        });
        return;
      }
    
      Adapter.get().http({
        method: "GET",
        url: stripTrailingSlash(fhirServiceUrl) + "/metadata"
      }).then(
        function(r){
          var res = {
            "name": "SMART on FHIR Testing Server",
            "description": "Dev server for SMART on FHIR",
            "url": fhirServiceUrl,
            "oauth2": {
              "registration_uri": null,
              "authorize_uri": null,
              "token_uri": null
            }
          };
    
          try {
            var smartExtension = r.rest[0].security.extension.filter(function (e) {
               return (e.url === "http://fhir-registry.smarthealthit.org/StructureDefinition/oauth-uris");
            });
    
            smartExtension[0].extension.forEach(function(arg, index, array){
              if (arg.url === "register") {
                res.oauth2.registration_uri = arg.valueUri;
              } else if (arg.url === "authorize") {
                res.oauth2.authorize_uri = arg.valueUri;
              } else if (arg.url === "token") {
                res.oauth2.token_uri = arg.valueUri;
              }
            });
          }
          catch (err) {
            return errback && errback(err);
          }
    
          callback && callback(res);
        }, function() {
            errback && errback("Unable to fetch conformance statement");
        }
      );
    };
    
    var noAuthFhirProvider = function(serviceUrl){
      return {
        "oauth2": null,
        "url": serviceUrl
      }
    };
    
    function relative(url){
      return (window.location.protocol + "//" + window.location.host + window.location.pathname).match(/(.*\/)[^\/]*/)[1] + url;
    }
    
    function isBypassOAuth(){
      return (urlParam("fhirServiceUrl") && !(urlParam("iss")));
    }
    
    function bypassOAuth(fhirServiceUrl, callback){
      callback && callback({
        "oauth2": null,
        "url": fhirServiceUrl || urlParam("fhirServiceUrl")
      });
    }
    
    BBClient.authorize = function(params, errback){
    
      if (!errback){
        errback = function(){
            console.log("Failed to discover authorization URL given", params);
        };
      }
      
      // prevent inheritance of tokenResponse from parent window
      delete sessionStorage.tokenResponse;
    
      if (!params.client){
        params = {
          client: params
        };
      }
    
      if (!params.response_type){
        params.response_type = 'code';
      }
    
       if (!params.client.redirect_uri){
        params.client.redirect_uri = relative("");
      }
    
      if (!params.client.redirect_uri.match(/:\/\//)){
        params.client.redirect_uri = relative(params.client.redirect_uri);
      }
    
      var launch = urlParam("launch");
      if (launch){
        if (!params.client.scope.match(/launch/)){
          params.client.scope += " launch";
        }
        params.client.launch = launch;
      }
    
      var server = urlParam("iss") || urlParam("fhirServiceUrl");
      if (server){
        if (!params.server){
          params.server = server;
        }
      }
    
      if (urlParam("patientId")){
        params.fake_token_response = params.fake_token_response || {};
        params.fake_token_response.patient = urlParam("patientId");
      }
    
      providers(params.server, params.provider, function(provider){
    
        params.provider = provider;
    
        var state = params.client.state || Guid.newGuid();
        var client = params.client;
    
        if (params.provider.oauth2 == null) {
    
          // Adding state to tokenResponse object
          if (BBClient.settings.fullSessionStorageSupport) { 
            sessionStorage[state] = JSON.stringify(params);
            sessionStorage.tokenResponse = JSON.stringify({state: state});
          } else {
            var combinedObject = $.extend(true, params, { 'tokenResponse' : {state: state} });
            sessionStorage[state] = JSON.stringify(combinedObject);
          }
    
          window.location.href = client.redirect_uri + "?state="+encodeURIComponent(state);
          return;
        }
        
        sessionStorage[state] = JSON.stringify(params);
    
        console.log("sending client reg", params.client);
    
        var redirect_to=params.provider.oauth2.authorize_uri + "?" + 
          "client_id="+encodeURIComponent(client.client_id)+"&"+
          "response_type="+encodeURIComponent(params.response_type)+"&"+
          "scope="+encodeURIComponent(client.scope)+"&"+
          "redirect_uri="+encodeURIComponent(client.redirect_uri)+"&"+
          "state="+encodeURIComponent(state)+"&"+
          "aud="+encodeURIComponent(params.server);
        
        if (typeof client.launch !== 'undefined' && client.launch) {
           redirect_to += "&launch="+encodeURIComponent(client.launch);
        }
    
        window.location.href = redirect_to;
      }, errback);
    };
    
    BBClient.resolveAuthType = function (fhirServiceUrl, callback, errback) {
    
          Adapter.get().http({
             method: "GET",
             url: stripTrailingSlash(fhirServiceUrl) + "/metadata"
          }).then(function(r){
              var type = "none";
              
              try {
                if (r.rest[0].security.service[0].coding[0].code.toLowerCase() === "smart-on-fhir") {
                    type = "oauth2";
                }
              }
              catch (err) {
              }
    
              callback && callback(type);
            }, function() {
               errback && errback("Unable to fetch conformance statement");
          });
    };
    
    }).call(this,require('_process'))
    },{"./adapter":45,"./client":47,"./guid":49,"_process":22,"jsonwebtoken":40}],47:[function(require,module,exports){
    var btoa = require('btoa');
    var Adapter = require('./adapter');
    
    module.exports = FhirClient;
    
    function ClientPrototype(){};
    var clientUtils = require('./utils');
    Object.keys(clientUtils).forEach(function(k){
      ClientPrototype.prototype[k] = clientUtils[k];
    });
    
    function FhirClient(p) {
      // p.serviceUrl
      // p.auth {
        //    type: 'none' | 'basic' | 'bearer'
        //    basic --> username, password
        //    bearer --> token
        // }
    
        var client = new ClientPrototype();
        var fhir = Adapter.get().fhirjs;
    
        var server = client.server = {
          serviceUrl: p.serviceUrl,
          auth: p.auth || {type: 'none'}
        }
        
        var auth = {};
        
        if (server.auth.type === 'basic') {
            auth = {
                user: server.auth.username,
                pass: server.auth.password
            };
        } else if (server.auth.type === 'bearer') {
            auth = {
                bearer: server.auth.token
            };
        }
        
        client.api = fhir({
            baseUrl: server.serviceUrl,
            auth: auth
        });
        
        if (p.patientId) {
            client.patient = {};
            client.patient.id = p.patientId;
            client.patient.api = fhir({
                baseUrl: server.serviceUrl,
                auth: auth,
                patient: p.patientId
            });
            client.patient.read = function(){
                return client.get({resource: 'Patient'});
            };
        }
        
        var fhirAPI = (client.patient)?client.patient.api:client.api;
    
        client.userId = p.userId;
    
        server.auth = server.auth ||  {
          type: 'none'
        };
    
        if (!client.server.serviceUrl || !client.server.serviceUrl.match(/https?:\/\/.+[^\/]$/)) {
          throw "Must supply a `server` property whose `serviceUrl` begins with http(s) " + 
            "and does NOT include a trailing slash. E.g. `https://fhir.aws.af.cm/fhir`";
        }
        
        client.authenticated = function(p) {
          if (server.auth.type === 'none') {
            return p;
          }
    
          var h;
          if (server.auth.type === 'basic') {
            h = "Basic " + btoa(server.auth.username + ":" + server.auth.password);
          } else if (server.auth.type === 'bearer') {
            h = "Bearer " + server.auth.token;
          }
          if (!p.headers) {p.headers = {};}
          p.headers['Authorization'] = h
          //p.beforeSend = function (xhr) { xhr.setRequestHeader ("Authorization", h); }
    
          return p;
        };
    
        client.get = function(p) {
            var ret = Adapter.get().defer();
            var params = {type: p.resource};
            
            if (p.id) {
                params["id"] = p.id;
            }
              
            fhirAPI.read(params)
                .then(function(res){
                    ret.resolve(res.data);
                }, function(){
                    ret.reject("Could not fetch " + p.resource + " " + p.id);
                });
              
            return ret.promise;
        };
    
        client.user = {
          'read': function(){
            var userId = client.userId;
            resource = userId.split("/")[0];
            uid = userId.split("/")[1];
            return client.get({resource: resource, id: uid});
          }
        };
    
        function absolute(path, server) {
          if (path.match(/^http/)) return path;
          if (path.match(/^urn/)) return path;
    
          // strip leading slash
          if (path.charAt(0) == "/") path = path.substr(1);
    
          return server.serviceUrl + '/' + path;
        }
    
        client.getBinary = function(url) {
    
          var ret = Adapter.get().defer();
    
          Adapter.get().http(client.authenticated({
            type: 'GET',
            url: url,
            dataType: 'blob'
          }))
          .done(function(blob){
            ret.resolve(blob);
          })
          .fail(function(){
            ret.reject("Could not fetch " + url, arguments);
          });
          return ret.promise;
        };
    
        client.fetchBinary = function(path) {
            var url = absolute(path, server);
            return client.getBinary(url);
        };
    
        return client;
    }
    
    },{"./adapter":45,"./utils":50,"btoa":38}],48:[function(require,module,exports){
    var client = require('./client');
    var oauth2 = require('./bb-client');
    var adapter = require('./adapter');
    
    window.FHIR = {
      client: client,
      oauth2: oauth2
    };
    
    module.exports = adapter.set;
    },{"./adapter":45,"./bb-client":46,"./client":47}],49:[function(require,module,exports){
    var EMPTY = '00000000-0000-0000-0000-000000000000';
    
    var _padLeft = function (paddingString, width, replacementChar) {
      return paddingString.length >= width ? paddingString : _padLeft(replacementChar + paddingString, width, replacementChar || ' ');
    };
    
    var _s4 = function (number) {
      var hexadecimalResult = number.toString(16);
      return _padLeft(hexadecimalResult, 4, '0');
    };
    
    var _cryptoGuid = function () {
      var buffer = new window.Uint16Array(8);
      window.crypto.getRandomValues(buffer);
      return [_s4(buffer[0]) + _s4(buffer[1]), _s4(buffer[2]), _s4(buffer[3]), _s4(buffer[4]), _s4(buffer[5]) + _s4(buffer[6]) + _s4(buffer[7])].join('-');
    };
    
    var _guid = function () {
      var currentDateMilliseconds = new Date().getTime();
      return 'xxxxxxxx-xxxx-4xxx-yxxx-xxxxxxxxxxxx'.replace(/[xy]/g, function (currentChar) {
        var randomChar = (currentDateMilliseconds + Math.random() * 16) % 16 | 0;
        currentDateMilliseconds = Math.floor(currentDateMilliseconds / 16);
        return (currentChar === 'x' ? randomChar : (randomChar & 0x7 | 0x8)).toString(16);
      });
    };
    
    var create = function () {
      var hasCrypto = typeof (window.crypto) != 'undefined',
      hasRandomValues = hasCrypto && typeof (window.crypto.getRandomValues) != 'undefined';
      return (hasCrypto && hasRandomValues) ? _cryptoGuid() : _guid();
    };
    
    module.exports =  {
      newGuid: create,
      empty: EMPTY
    };
    
    },{}],50:[function(require,module,exports){
    var utils = module.exports =  {};
    
    utils.byCodes = function(observations, property){
    
      var bank = utils.byCode(observations, property);
      function byCodes(){
        var ret = [];
        for (var i=0; i<arguments.length;i++){
          var set = bank[arguments[i]];
          if (set) {[].push.apply(ret, set);}
        }
        return ret;
      }
    
      return byCodes;
    };
    
    utils.byCode = function(observations, property){
      var ret = {};
      if (!Array.isArray(observations)){
        observations = [observations];
      }
      observations.forEach(function(o){
        if (o.resourceType === "Observation"){
          if (o[property] && Array.isArray(o[property].coding)) {
            o[property].coding.forEach(function (coding){
              ret[coding.code] = ret[coding.code] || [];
              ret[coding.code].push(o);
            });
          }
        }
      });
      return ret;
    };
    
    function ensureNumerical(pq) {
      if (typeof pq.value !== "number") {
        throw "Found a non-numerical unit: " + pq.value + " " + pq.code;
      }
    };
    
    utils.units = {
      cm: function(pq){
        ensureNumerical(pq);
        if(pq.code == "cm") return pq.value;
        if(pq.code == "m") return 100*pq.value;
        if(pq.code == "in") return 2.54*pq.value;
        if(pq.code == "[in_us]") return 2.54*pq.value;
        if(pq.code == "[in_i]") return 2.54*pq.value;
        if(pq.code == "ft") return 30.48*pq.value;
        if(pq.code == "[ft_us]") return 30.48*pq.value;
        throw "Unrecognized length unit: " + pq.code
      },
      kg: function(pq){
        ensureNumerical(pq);
        if(pq.code == "kg") return pq.value;
        if(pq.code == "g") return pq.value / 1000;
        if(pq.code.match(/lb/)) return pq.value / 2.20462;
        if(pq.code.match(/oz/)) return pq.value / 35.274;
        throw "Unrecognized weight unit: " + pq.code
      },
      any: function(pq){
        ensureNumerical(pq);
        return pq.value
      }
    };
    
    
    
    },{}]},{},[44]);
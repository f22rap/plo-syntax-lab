'use strict';
const Ajv = require('ajv/dist/2020'), addFormats = require('ajv-formats');
const catalog = require('../../docs/mcp-tools.schema.json');
const ajv = new Ajv({allErrors: true, strict: false});
addFormats(ajv);
const validators = new Map(catalog.tools.map(t => [t.name, {input: ajv.compile(t.inputSchema), output: ajv.compile(t.outputSchema)}]));
class ApiError extends Error {
 constructor(code, message, details = {}, retryable = false) { super(message); Object.assign(this,{code,details,retryable}); }
}
function validate(name, kind, value) {
 const v = validators.get(name)?.[kind];
 if (!v) throw new ApiError('UNKNOWN_TOOL','未定義のツールです。');
 if (!v(value)) throw new ApiError(kind === 'input' ? 'INVALID_ARGUMENT' : 'INTERNAL_ERROR', kind === 'input' ? '引数がツールの定義と一致しません。' : '出力が定義と一致しません。', kind === 'input' ? {issues: v.errors.map(e=>({path:e.instancePath,rule:e.keyword}))} : {});
}
module.exports = {catalog, validate, ApiError};

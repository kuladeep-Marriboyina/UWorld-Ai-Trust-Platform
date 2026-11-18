/** Plain-English rendering of an authored rule, so people read the logic instead of JSON. */
export const OPS: { [k: string]: string } = {
  eq: 'is', ne: 'is not', gt: '>', lt: '<', gte: '≥', lte: '≤', in: 'is one of', notin: 'is not one of',
  contains: 'contains', startswith: 'starts with', isnull: 'is empty', notnull: 'is present',
};

export function condText(c: any): string {
  if (!c || !c.field) { return '…'; }
  const v = c.op === 'in' || c.op === 'notin' ? '[' + (c.values || []).join(', ') + ']' : c.op === 'isnull' || c.op === 'notnull' ? '' : `“${c.value ?? ''}”`;
  return `${c.field} ${OPS[c.op] || c.op} ${v}`.trim();
}

export function describeLogic(l: any): string {
  if (!l || !l.target) { return 'Choose a table to start.'; }
  const where = (l.when || []).length ? ` where ${(l.when || []).map(condText).join(' AND ')}` : '';
  const except = (l.exclusions || []).length ? `, excluding rows where ${(l.exclusions || []).map(condText).join(' OR ')}` : '';
  if (!l.groupBy) {
    return `Raise a ${l.severity || 'Medium'} case for every ${l.target} row${where}${except}.`;
  }
  const agg = l.aggregate === 'distinct' ? `${l.thresholdValue ?? 'N'}+ distinct ${l.aggregateField || '…'}` : l.aggregate === 'sum' ? `a total ${l.aggregateField || '…'} of ${l.thresholdValue ?? 'N'}+` : `${l.thresholdValue ?? 'N'}+ rows`;
  const win = l.windowMinutes ? ` within ${l.windowMinutes} minutes` : ' (any time)';
  return `Raise a ${l.severity || 'Medium'} case when one ${l.groupBy} has ${agg}${win} in ${l.target}${where}${except}.`;
}

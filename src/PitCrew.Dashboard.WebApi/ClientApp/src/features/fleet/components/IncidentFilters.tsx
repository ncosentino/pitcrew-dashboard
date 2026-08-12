import { Button } from '@/components/ui/button';
import { FilterChips, type FilterChipDescriptor } from '@/core/ui/FilterChips';
import { FilterToolbar } from '@/core/ui/FilterToolbar';
import { FormField } from '@/core/ui/FormField';

import type { IncidentSort, IncidentView, SeverityFilter } from '../incidentView';

const inputClassName = 'h-9 min-w-0 w-full rounded-md border bg-background px-3 text-sm';

interface IncidentFiltersProps {
  readonly view: IncidentView;
  readonly query: string;
  readonly severity: SeverityFilter;
  readonly sort: IncidentSort;
  readonly chips: ReadonlyArray<FilterChipDescriptor>;
  readonly resultSummary: string;
  readonly onParameterChange: (key: string, value: string, defaultValue: string) => void;
  readonly onReset: () => void;
  readonly onRefresh: () => void;
}

export function IncidentFilters({
  view,
  query,
  severity,
  sort,
  chips,
  resultSummary,
  onParameterChange,
  onReset,
  onRefresh,
}: IncidentFiltersProps) {
  return (
    <>
      <FilterToolbar label="Incident filters and summary">
        <FormField label="Work queue">
          <select
            className={inputClassName}
            value={view}
            onChange={(event) => onParameterChange('view', event.target.value, 'attention')}
          >
            <option value="attention">Needs attention</option>
            <option value="active">All active</option>
            <option value="resolved">Resolved</option>
            <option value="history">All history</option>
          </select>
        </FormField>
        <FormField label="Search incidents">
          <input
            className={inputClassName}
            type="search"
            value={query}
            onChange={(event) => onParameterChange('q', event.target.value, '')}
          />
        </FormField>
        <FormField label="Severity">
          <select
            className={inputClassName}
            value={severity}
            onChange={(event) => onParameterChange('severity', event.target.value, 'all')}
          >
            <option value="all">All severities</option>
            <option value="critical">Critical</option>
            <option value="warning">Warning</option>
          </select>
        </FormField>
        <FormField label="Sort by">
          <select
            className={inputClassName}
            value={sort}
            onChange={(event) => onParameterChange('sort', event.target.value, 'priority')}
          >
            <option value="priority">Priority</option>
            <option value="newest">Newest triggered</option>
            <option value="oldest">Oldest triggered</option>
            <option value="observed">Recently observed</option>
          </select>
        </FormField>
      </FilterToolbar>

      <div className="flex flex-wrap items-center justify-between gap-3">
        <FilterChips
          chips={chips}
          resultSummary={resultSummary}
          onClearAll={onReset}
          clearAllLabel="Reset incident view"
        />
        <Button type="button" size="sm" variant="outline" onClick={onRefresh}>
          Refresh
        </Button>
      </div>
    </>
  );
}

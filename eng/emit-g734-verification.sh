#!/usr/bin/env bash
set -euo pipefail

results_directory=""
verification_directory=""
head_sha=""
run_id=""
repository=""

while (($# > 0)); do
  case "$1" in
    --results-directory)
      results_directory="$2"
      shift 2
      ;;
    --verification-directory)
      verification_directory="$2"
      shift 2
      ;;
    --head-sha)
      head_sha="$2"
      shift 2
      ;;
    --run-id)
      run_id="$2"
      shift 2
      ;;
    --repository)
      repository="$2"
      shift 2
      ;;
    *)
      echo "unknown argument: $1" >&2
      exit 2
      ;;
  esac
done

if [[ -z "${results_directory}" || -z "${verification_directory}" || -z "${head_sha}" || -z "${run_id}" || -z "${repository}" ]]; then
  echo "results, verification, head, run, and repository inputs are required" >&2
  exit 2
fi

command -v jq >/dev/null 2>&1 || { echo "jq is required to emit the G734 verification artifact" >&2; exit 2; }
shopt -s nullglob
trx_files=("${results_directory}"/*.trx)
[[ ${#trx_files[@]} -gt 0 ]] || { echo "no TRX files found in ${results_directory}" >&2; exit 1; }
[[ -s "${verification_directory}/density.json" ]] || { echo "density evidence was not emitted" >&2; exit 1; }
[[ -s "${verification_directory}/live.json" ]] || { echo "live evidence was not emitted" >&2; exit 1; }

count_matches() {
  local pattern="$1"
  (grep -h -o -- "${pattern}" "${trx_files[@]}" || true) | wc -l | tr -d ' '
}

passed_count="$(count_matches 'outcome="Passed"')"
not_executed_count="$(count_matches 'outcome="NotExecuted"')"
skipped_count="$(($(count_matches 'outcome="Skipped"') + not_executed_count))"
failed_count="$(count_matches 'outcome="Failed"')"
focused_count="$(count_matches 'testName="[^"]*NotifySupervisionShrinkG734Tests[^"]*"')"

(( passed_count > 0 )) || { echo "TRX contains no passed tests" >&2; exit 1; }
(( failed_count == 0 )) || { echo "TRX contains failed tests" >&2; exit 1; }
(( focused_count == 15 )) || { echo "expected 15 G734 focused tests, found ${focused_count}" >&2; exit 1; }

ci_run_url=""
if [[ "${run_id}" != "local" ]]; then
  ci_run_url="https://github.com/${repository}/actions/runs/${run_id}"
fi

output_path="${verification_directory}/g734-verification.json"
temporary_path="${output_path}.tmp"
jq -n \
  --slurpfile density "${verification_directory}/density.json" \
  --slurpfile live "${verification_directory}/live.json" \
  --arg repository "${repository}" \
  --arg head_sha "${head_sha}" \
  --arg run_id "${run_id}" \
  --arg run_url "${ci_run_url}" \
  --argjson passed "${passed_count}" \
  --argjson skipped "${skipped_count}" \
  --argjson failed "${failed_count}" \
  --argjson focused "${focused_count}" \
  '(
    $density[0] as $d |
    $live[0] as $l |
    if ($d.before_bytes - $d.after_bytes) != ($d.invariant_text.net_record_bytes_saved + $d.invariant_text.other_record_bytes_saved)
    then error("density savings do not reconcile")
    elif $d.before_bytes != 50735552 or $d.after_bytes != 48561944 or $d.record_count != 10063
      or $d.invariant_text.literal_bytes_removed_from_records != 2435246
      or $d.invariant_text.reference_bytes_added_to_records != 322016
      or $d.invariant_text.net_record_bytes_saved != 2173608
      or $d.invariant_text.other_record_bytes_saved != 0
    then error("deterministic density fixture no longer matches the committed verification contract")
    elif $l.shrink.supervisor_state != "running"
    then error("live shrink did not observe a running supervisor")
    elif $l.next_cycle_appended != true or $l.cycle_count_after <= $l.cycle_count_before
    then error("live transcript does not prove the next cycle append")
    elif $l.shrink.before_record_count != $l.shrink.after_record_count
    then error("live shrink changed the retained record count")
    else {
      schema: "intent-system.g734-supervision-verification/v1",
      source: {
        repository: $repository,
        head_sha: $head_sha,
        ci_run_id: $run_id,
        ci_run_url: $run_url,
        exact_head_rule: "consume this artifact only when source.head_sha equals the reviewed commit under test"
      },
      tests: {
        source_contract: {
          passed: $passed,
          skipped: $skipped,
          failed: $failed
        },
        focused_g734: $focused,
        focused_class: "NotifySupervisionShrinkG734Tests"
      },
      density: $d,
      live: {
        supervisor_process_state: $l.supervisor_process_state,
        supervisor_state_at_shrink: $l.shrink.supervisor_state,
        cycle_count_before: $l.cycle_count_before,
        cycle_count_after: $l.cycle_count_after,
        next_cycle_appended: $l.next_cycle_appended,
        timestamp_dependent_fields: $l.timestamp_dependent_fields,
        shrink: $l.shrink
      },
      integrity: {
        numeric_values_source: "density.json and live.json emitted by the focused tests; no live byte count is hardcoded in the committed document",
        invariant_saving: $d.invariant_text.net_record_bytes_saved,
        invariant_saving_matches_byte_delta: (($d.before_bytes - $d.after_bytes) == $d.invariant_text.net_record_bytes_saved),
        literal_minus_reference_occurrence_delta: ($d.invariant_text.literal_bytes_removed_from_records - $d.invariant_text.reference_bytes_added_to_records),
        accounting_note: "literal_bytes_removed_from_records and reference_bytes_added_to_records are diagnostic occurrence counters; the direct byte delta and net_record_bytes_saved include the serialized field/prefix changes required to replace the invariant payload",
        audit_outcome: $l.shrink.audit.record.outcome,
        records_archived: $l.shrink.audit.records_archived,
        records_discarded: $l.shrink.audit.records_discarded,
        records_rotated: $l.shrink.audit.records_rotated
      }
    }
    end
  )' > "${temporary_path}"
mv "${temporary_path}" "${output_path}"

checksum_name="$(basename "${output_path}")"
checksum_directory="$(dirname "${output_path}")"
if command -v shasum >/dev/null 2>&1; then
  (
    cd "${checksum_directory}"
    shasum -a 256 "${checksum_name}"
  ) > "${output_path}.sha256"
else
  (
    cd "${checksum_directory}"
    sha256sum "${checksum_name}"
  ) > "${output_path}.sha256"
fi

echo "G734 verification artifact: ${output_path}"
cat "${output_path}"

#!/usr/bin/env bash

set -euo pipefail

usage() {
  cat <<'EOF'
Usage:
  release-reachability.sh --commit <commit-or-ref> --default-branch <branch> [--repo-root <path>]
  release-reachability.sh --survey --default-branch <branch> [--repo-root <path>]
EOF
}

mode=check
commit_or_ref=
default_branch=
repo_root="${RELEASE_REPO_ROOT:-$(pwd)}"

while (($# > 0)); do
  case "$1" in
    --commit)
      (($# >= 2)) || { echo "release-reachability: --commit requires a value" >&2; exit 2; }
      commit_or_ref="$2"
      shift 2
      ;;
    --default-branch)
      (($# >= 2)) || { echo "release-reachability: --default-branch requires a value" >&2; exit 2; }
      default_branch="$2"
      shift 2
      ;;
    --repo-root)
      (($# >= 2)) || { echo "release-reachability: --repo-root requires a value" >&2; exit 2; }
      repo_root="$2"
      shift 2
      ;;
    --survey)
      mode=survey
      shift
      ;;
    --help|-h)
      usage
      exit 0
      ;;
    *)
      echo "release-reachability: unknown argument: $1" >&2
      usage >&2
      exit 2
      ;;
  esac
done

if [[ -z "$default_branch" ]]; then
  echo "release-reachability: --default-branch is required" >&2
  exit 2
fi

if [[ "$mode" == check && -z "$commit_or_ref" ]]; then
  echo "release-reachability: --commit is required unless --survey is used" >&2
  exit 2
fi

cd -- "$repo_root"

if ! git rev-parse --git-dir >/dev/null 2>&1; then
  echo "release-reachability: repository root is not a Git repository: $repo_root" >&2
  exit 2
fi

default_ref="refs/remotes/origin/$default_branch"
if ! git show-ref --verify --quiet "$default_ref"; then
  default_ref="refs/heads/$default_branch"
fi

if ! git show-ref --verify --quiet "$default_ref"; then
  echo "release-reachability: default branch ref not found: $default_branch" >&2
  exit 2
fi

default_commit="$(git rev-parse --verify "$default_ref^{commit}")"

if [[ "$mode" == check ]]; then
  if ! commit="$(git rev-parse --verify "$commit_or_ref^{commit}" 2>/dev/null)"; then
    echo "release-reachability: commit or ref not found: $commit_or_ref" >&2
    exit 2
  fi

  if git merge-base --is-ancestor "$commit" "$default_commit"; then
    echo "release-reachability: reachable commit=$commit default_branch=$default_branch default_ref=$default_ref default_commit=$default_commit ordinary_path=non-interactive"
    exit 0
  fi

  echo "release-reachability: REFUSED commit=$commit default_branch=$default_branch default_ref=$default_ref default_commit=$default_commit"
  echo "consequence: the repository default branch will not contain the released source until this commit lands; no release build or publish may proceed."
  echo "action: land the commit on the repository default branch, then rerun this gate before creating or publishing the release tag."
  exit 1
fi

total=0
reachable=0
unreachable=0
unresolved=0

while IFS= read -r tag; do
  [[ -n "$tag" ]] || continue
  total=$((total + 1))
  if ! tag_commit="$(git rev-parse --verify "$tag^{commit}" 2>/dev/null)"; then
    unresolved=$((unresolved + 1))
    echo "release-tag-survey: tag=$tag result=unresolved"
    continue
  fi

  if git merge-base --is-ancestor "$tag_commit" "$default_commit"; then
    reachable=$((reachable + 1))
    echo "release-tag-survey: tag=$tag commit=$tag_commit result=reachable"
  else
    unreachable=$((unreachable + 1))
    echo "release-tag-survey: tag=$tag commit=$tag_commit result=unreachable"
  fi
done < <(git tag --list 'v*' --sort=version:refname)

echo "release-tag-survey: total=$total reachable=$reachable unreachable=$unreachable unresolved=$unresolved default_branch=$default_branch default_ref=$default_ref default_commit=$default_commit"

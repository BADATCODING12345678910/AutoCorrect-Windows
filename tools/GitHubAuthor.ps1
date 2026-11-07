# Shared Git author for public AutoCorrect-Windows commits.
# Uses the GitHub noreply address tied to BADATCODING12345678910 so
# contributions show on that profile (no personal email required).

$script:AcGitHubAuthorName = "Kyle Welch"
$script:AcGitHubAuthorEmail = "183003052+BADATCODING12345678910@users.noreply.github.com"

function Invoke-AcGitCommit {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Message
    )
    git -c "user.name=$script:AcGitHubAuthorName" `
        -c "user.email=$script:AcGitHubAuthorEmail" `
        commit -m $Message
}

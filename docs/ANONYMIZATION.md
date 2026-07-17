# Publishing this review artifact anonymously

This directory is already prepared without Git history. Publish it as a new repository or upload it to the conference artifact service; do not fork the development repository and do not preserve its commits.

Recommended GitHub setup for double-blind review:

1. Register a neutral, long-lived account using an email address that is not tied to an institution, employer, personal domain, or reused public username. Avoid temporary/disposable inboxes because GitHub may refuse verification.
2. Leave profile name, bio, location, links, social accounts, and custom avatar empty. A reused Gravatar or avatar can identify the author.
3. Enable **Keep my email addresses private** and **Block command-line pushes that expose my email** in GitHub email settings.
4. Configure the repository clone to use the account's GitHub-generated `ID+USERNAME@users.noreply.github.com` commit address. Verify the author name and email before the first push.
5. Create an empty repository, then initialize this extracted directory as a new history. Do not add a remote pointing to the development repository.
6. Run `python scripts/anonymization_scan.py` and inspect `git log --format=fuller` before publishing.
7. Use a neutral repository name and description. Do not mention the laboratory, institution, course, supervisor, city, or funding account.

This protects anonymity from ordinary reviewers and public visitors. It does not make the registrant anonymous to the hosting platform, network operator, payment provider, or lawful process. If conference policy provides an official anonymous artifact host, prefer that service.

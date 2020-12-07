# Rules of Thumb for Code Reviews

This document aims to outline a set of best practices that are followed, within reason, when reviewing PRs.

## Select a reviewer

Author:
- Manually assign at least one reviewer when creating a PR
- Let the reviewer know that you have selected them during scrum (or ping them over Teams)

Reviewer:
- Indicate right away if you are the wrong reviewer (you won't be able to review the PR if you are going on vacation, for example)

Anyone can review a PR, even if you are not assigned as the reviewer.

## Keep PRs small if possible

- A PR should not bundle multiple changes
- A PR could include one change that touches multiple files
- Try to split things out, if possible
  - Consider putting up two PRs, one for the refactoring work and one for feature development
  - Consider putting up a separate PR for a bug fix if you come across a bug during feature development
- If necessary, use a feature branch to allow for incremental reviews of a big feature

## Provide enough context

- Provide enough context in the PR's description
  - Someone on the team who hasn’t worked on the feature before should be able to understand the PR
  - If applicable, add additional information about how we would do acceptance for the PR change (if it isn’t in the user story)
  - If possible, add examples of API calls that could be made to test the PR change
- Tag your PRs with the appropriate labels to help provide context for the release notes (see [Squash Merge Requirements](https://github.com/microsoft/fhir-server/blob/master/SquashMergeRequirements.md) for more details)

## Make comments actionable

|Instead of This|Try This|Reasoning|
|---|---|---|
|“This method is too long”|“This method is quite long, maybe you could extract this part as a sub method”|Provide a suggestion|
|“I don't understand this”|“It's hard to understand this because I don't see why we need to use a dictionary here instead of a list”|Ask specific questions|
|“This method should be async”|“This method should be async, otherwise it could cause deadlocks”|Explain why|
|“Hey, you should remove this line”|“Marking this as a must-fix before merging because this line will break the server/client connection.”|Prioritize your comments|
|“Maybe try a different name for this variable”|“It would be good to rename this to something like `CurrencyValueSet` to avoid the name `SystemValues`, since the `ValueSet` uri is "http://hl7.org/fhir/ValueSet/currencies" and the `CodeSystem` is "urn:iso:std:iso:4217" ([here](https://www.hl7.org/fhir/valueset-currencies.html) is the relevant part of the spec).”|Help suggest a new name if you think a variable could be renamed|

## Ask questions to gain understanding

- Not all comments are actionable
- Preface non-actionable comments with "for my understanding"
- It's important to continue to ask these questions

## Practice healthy communication

|Instead of This|Try This|Reasoning|
|---|---|---|
|"You forgot to add method comment"|"This method is missing a comment"|Avoid words like "mine and "yours"|
|“You should use a constant”|"Here it seems we need a constant because..."|Use the word "we"|
|"Rename to user_id"|"What do you think of renaming to user_id?"|If fitting, ask questions instead of making demands|

## Be humble and assume the best of the author

- Ask questions before categorizing something as an error

## Move long PR discussions to video calls

- Be open to moving discussions to chat or video calls for clarification
- Post a resolution comment so other people reading the PR know what happened

## Positive reinforcement is important!

- If you think it is appropriate and would like to add an emoji, the team has expressed that we like them 🙂

## Additional notes

- Consider using [Code Flow](https://www.1eswiki.com/wiki/CodeFlow_integration_with_GitHub_Pull_Requests), a tool that integrates with Github to provide a helpful UI for code reviewing
- If the same issue occurs in multiple places in one file, add one comment that mentions that this occurs more than once (reconsider for multiple files)
- It is nice to proceed nitpick comments with “nit”
- The thumbs up reaction can be used for acknowledging a comment in a PR discussion


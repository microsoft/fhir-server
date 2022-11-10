// Inspired by our friends at Inferno:
// https://github.com/inferno-framework/inferno-reference-server/tree/master/src/main/webapp

// Import our custom CSS
import '../scss/styles.scss'

function authorize_click()
{
  let urlParams = new URLSearchParams(window.location.search);

  let newUrl = urlParams.get('aud') || '';
  newUrl = urlParams.get('aud') + '/authorize?user=true'

  newUrl = newUrl + "&response_type=code";
  newUrl = newUrl + "&client_id=" + urlParams.get('client_id') || '';
  newUrl = newUrl + "&redirect_uri=" + urlParams.get('redirect_uri') || '';

  let checkedScopes = [...document.querySelectorAll('.ct:checked')].map(e => e.value).join(" ");
  newUrl = newUrl + "&scope=" + checkedScopes || '';
  
  newUrl = newUrl + "&state=" + urlParams.get('state') || '';
  newUrl = newUrl + "&aud=" + urlParams.get('aud') || '';

  let code_challenge = urlParams.get('code_challenge') || '';
  if (code_challenge.length > 0)
  {
    newUrl = newUrl + "&code_challenge=" + code_challenge || '';
  }

  let code_challenge_method = urlParams.get('code_challenge_method') || '';
  if (code_challenge_method.length > 0)
  {
    newUrl = newUrl + "&code_challenge_method=" + code_challenge_method || '';
  }

  window.location.href = newUrl
}

document.addEventListener('DOMContentLoaded', function () {
  // Fetch scopes
  let urlParams = new URLSearchParams(window.location.search);
  let scopes = urlParams.get('scope') || '';
  scopes = scopes.trim();
  let scopesList = scopes.split(' ').sort();

  var scopesContainer = document.getElementById("scopesContainer");
  
  for (let i = 0; i < scopesList.length; i++)
  {
    let scope = scopesList[i];
    let scopeId = "scope-" + i;

    if (scope === '')
    {
      continue;
    }

    // create checkbox container
    let container = document.createElement('li');
    container.className = 'list-group-item';

    // create checkbox
    let checkbox = document.createElement('input');
    checkbox.type = "checkbox";
    checkbox.value = scope;
    checkbox.id = scopeId;
    checkbox.className = "form-check-input me-2 ct";
    checkbox.checked = true;

    // create label and add checkbox
    let label = document.createElement('label');
    label.className = 'list-group-item';
    label.appendChild(checkbox);
    label.appendChild(document.createTextNode(scope));

    // hide non patient and user scopes
    if (!scope.startsWith("patient") && !scope.startsWith("user"))
    {
      label.hidden = true;
    }

    // Add the scopes to the container
    scopesContainer.appendChild(label);
  }

  document.getElementById("authorizeButton").addEventListener("click", authorize_click)

});
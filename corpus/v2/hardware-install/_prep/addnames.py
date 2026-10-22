import json
c = json.load(open('hw-curated.json'))
c['names'].update({
    "Gaojia Optotech": "Optotech Lens Works", "Disney Train": "Storybook Train", "Droid Commander": "Robot Commander",
    "Ideas Piano": "Makers Piano",
})
json.dump(c, open('hw-curated.json', 'w'), indent=1)
print(len(c['names']))
